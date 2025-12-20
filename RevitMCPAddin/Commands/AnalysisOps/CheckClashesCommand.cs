// ================================================================
// File: Commands/AnalysisOps/CheckClashesCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Clash detection - fast AABB(bbox) and precise Solid intersection
// Units  : 入力 tolerance は mm、出力も SI （Length=mm, Volume=mm3）
// Notes  : pairs は AABB でプレフィルタ → solid の場合のみ BooleanIntersect
//          viewId 指定時は BoundingBox(view) を優先
//          namesOnly=true で軽量レスポンス
// Depends: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//          RevitMCPAddin.Core (UnitHelper, ResultUtil, RequestCommand, IRevitCommandHandler)
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    public class CheckClashesCommand : IRevitCommandHandler
    {
        public string CommandName => "check_clashes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            // ---- Params ----
            var p = (JObject)cmd.Params ?? new JObject();
            var method = (p.Value<string>("method") ?? "bbox").ToLowerInvariant();          // "bbox" | "solid"
            var toleranceMm = p.Value<double?>("toleranceMm") ?? 0.0;
            var minVolumeMm3 = p.Value<double?>("minVolumeMm3") ?? 0.0;                    // solid 用
            var maxPairs = Math.Max(1, p.Value<int?>("maxPairs") ?? 100000);
            var namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var viewIdOpt = p.Value<int?>("viewId");
            View view = null;
            if (viewIdOpt.HasValue)
            {
                var v = doc.GetElement(new ElementId(viewIdOpt.Value)) as View;
                if (v != null) view = v;
            }

            // 対象要素
            var ids = new List<ElementId>();
            var arr = p["elementIds"] as JArray;
            if (arr != null && arr.Count > 0)
            {
                foreach (var t in arr) ids.Add(new ElementId(t.Value<int>()));
            }
            else
            {
                // elementIds 未指定 → ビュー内可視要素（負荷を鑑み、モデル要素中心に絞る）
                var collector = (view != null)
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);
                ids = collector
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                    .Select(e => e.Id)
                    .ToList();
            }

            // Gather targets with AABB (ft)
            var entries = new List<Tgt>();
            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                if (e == null) continue;
                BoundingBoxXYZ bb = null;
                try { bb = (view != null) ? e.get_BoundingBox(view) : e.get_BoundingBox(null); } catch { }
                if (bb == null) continue;

                // inflate by tolerance (mm → ft)
                var inf = UnitHelper.MmToInternal(toleranceMm);
                var min = new XYZ(bb.Min.X - inf, bb.Min.Y - inf, bb.Min.Z - inf);
                var max = new XYZ(bb.Max.X + inf, bb.Max.Y + inf, bb.Max.Z + inf);

                // 無効な AABB はスキップ
                if (max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z) continue;

                entries.Add(new Tgt
                {
                    Id = e.Id,
                    CatId = e.Category?.Id?.IntegerValue ?? 0,
                    CatName = e.Category?.Name,
                    AabbMin = min,
                    AabbMax = max,
                    Elem = e
                });
            }

            // Sweep pairs with AABB test
            var clashes = new List<object>();
            long checkedPairs = 0;

            // 粗い最適化: 軸ごとにソートして早期break
            entries = entries.OrderBy(t => t.AabbMin.X).ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                var a = entries[i];
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var b = entries[j];

                    // x 軸分離: b.min.x > a.max.x なら以降は全て離れている
                    if (b.AabbMin.X > a.AabbMax.X) break;

                    checkedPairs++;
                    if (checkedPairs > maxPairs)
                        goto FINISH; // ガード発動：これ以上重くしない

                    if (!AabbIntersects(a, b)) continue;

                    if (method == "solid")
                    {
                        // AABB 交差 → Solid精査
                        var volFt3 = IntersectVolumeFt3(doc, a.Elem, b.Elem, view);
                        if (volFt3 <= 0) continue;

                        var volMm3 = UnitHelper.Ft3ToMm3(volFt3); // UnitHelper に実装済み:contentReference[oaicite:3]{index=3}
                        if (volMm3 < minVolumeMm3) continue;

                        clashes.Add(BuildClashPayloadSolid(a, b, volMm3, namesOnly));
                    }
                    else // bbox
                    {
                        // 交差AABB を算出
                        var interMin = new XYZ(
                            Math.Max(a.AabbMin.X, b.AabbMin.X),
                            Math.Max(a.AabbMin.Y, b.AabbMin.Y),
                            Math.Max(a.AabbMin.Z, b.AabbMin.Z));
                        var interMax = new XYZ(
                            Math.Min(a.AabbMax.X, b.AabbMax.X),
                            Math.Min(a.AabbMax.Y, b.AabbMax.Y),
                            Math.Min(a.AabbMax.Z, b.AabbMax.Z));

                        var dxMm = UnitHelper.FtToMm(Math.Max(0, interMax.X - interMin.X));
                        var dyMm = UnitHelper.FtToMm(Math.Max(0, interMax.Y - interMin.Y));
                        var dzMm = UnitHelper.FtToMm(Math.Max(0, interMax.Z - interMin.Z));
                        if (dxMm <= 0 || dyMm <= 0 || dzMm <= 0) continue;

                        var volMm3 = dxMm * dyMm * dzMm;

                        clashes.Add(BuildClashPayloadBbox(a, b, interMin, interMax, dxMm, dyMm, dzMm, volMm3, namesOnly));
                    }
                }
            }

        FINISH:
            var root = new JObject
            {
                ["ok"] = true,
                ["method"] = method,
                ["checkedPairs"] = checkedPairs,
                ["clashCount"] = clashes.Count,
                ["units"] = new JObject { ["Length"] = "mm", ["Volume"] = "mm3" },
                ["clashes"] = JToken.FromObject(clashes)
            };
            return root;
        }

        // ---- Data holder ----
        private class Tgt
        {
            public ElementId Id;
            public int CatId;
            public string CatName;
            public XYZ AabbMin;
            public XYZ AabbMax;
            public Element Elem;
        }

        // ---- AABB test ----
        private static bool AabbIntersects(Tgt a, Tgt b)
        {
            if (a.AabbMax.X <= b.AabbMin.X || b.AabbMax.X <= a.AabbMin.X) return false;
            if (a.AabbMax.Y <= b.AabbMin.Y || b.AabbMax.Y <= a.AabbMin.Y) return false;
            if (a.AabbMax.Z <= b.AabbMin.Z || b.AabbMax.Z <= a.AabbMin.Z) return false;
            return true;
        }

        // ---- Solid intersection volume (ft^3) ----
        private static double IntersectVolumeFt3(Document doc, Element ea, Element eb, View view)
        {
            try
            {
                var sa = CollectSolids(ea, view);
                var sb = CollectSolids(eb, view);
                if (sa.Count == 0 || sb.Count == 0) return 0.0;

                double total = 0.0;
                foreach (var a in sa)
                    foreach (var b in sb)
                    {
                        try
                        {
                            var res = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, BooleanOperationsType.Intersect);
                            if (res != null)
                            {
                                var vol = res.Volume; // ft^3
                                if (vol > 0) total += vol;
                            }
                        }
                        catch
                        {
                            // 幾何が壊れている/自明に交差しない 等はスキップ
                        }
                    }
                return total;
            }
            catch { return 0.0; }
        }

        private static List<Solid> CollectSolids(Element e, View view)
        {
            var solids = new List<Solid>();
            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    View = view
                };
                var geo = e.get_Geometry(opt);
                if (geo == null) return solids;

                var trf = Transform.Identity;
                PushSolids(geo, trf, solids);
            }
            catch { }
            return solids;
        }

        private static void PushSolids(GeometryElement ge, Transform t, List<Solid> outList)
        {
            foreach (var obj in ge)
            {
                switch (obj)
                {
                    case Solid s when s.Volume > 1e-9:
                        outList.Add(t == null || t.IsIdentity ? s : SolidUtils.CreateTransformed(s, t));
                        break;

                    case GeometryInstance gi:
                        var instTrf = gi.Transform;
                        var nextT = (t == null || t.IsIdentity) ? instTrf : t.Multiply(instTrf);
                        var ge2 = gi.GetInstanceGeometry();
                        if (ge2 != null) PushSolids(ge2, nextT, outList);
                        break;

                    case Curve _:
                    case Mesh _:
                    case PolyLine _:
                    default:
                        break;
                }
            }
        }

        // ---- payload builders ----
        private static object BuildIdCat(Tgt t, bool namesOnly) =>
            namesOnly ? (object)new { elementId = t.Id.IntegerValue }
                      : (object)new { elementId = t.Id.IntegerValue, categoryId = t.CatId, category = t.CatName };

        private static object BuildClashPayloadBbox(
            Tgt a, Tgt b, XYZ interMin, XYZ interMax,
            double dxMm, double dyMm, double dzMm, double volMm3, bool namesOnly)
        {
            var min = UnitPtMm(interMin);
            var max = UnitPtMm(interMax);
            return new
            {
                a = BuildIdCat(a, namesOnly),
                b = BuildIdCat(b, namesOnly),
                bbox = new { min, max },
                overlap = new { dx = dxMm, dy = dyMm, dz = dzMm, volumeMm3 = volMm3 },
                method = "bbox"
            };
        }

        private static object BuildClashPayloadSolid(Tgt a, Tgt b, double volMm3, bool namesOnly)
        {
            return new
            {
                a = BuildIdCat(a, namesOnly),
                b = BuildIdCat(b, namesOnly),
                overlap = new { volumeMm3 = volMm3 },
                method = "solid"
            };
        }

        private static object UnitPtMm(XYZ pFt) =>
            new { x = UnitHelper.FtToMm(pFt.X), y = UnitHelper.FtToMm(pFt.Y), z = UnitHelper.FtToMm(pFt.Z) };
    }
}
