#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    /// <summary>
    /// JSON-RPC: create_spatial_volume_overlay
    /// Rooms/Spaces の境界(CurveLoop)から押し出しSolidを作り、DirectShapeで可視化。
    /// ビュー単位で色/透過をオーバーライド適用。
    /// </summary>
    public class CreateSpatialVolumeOverlayCommand : IRevitCommandHandler
    {
        public string CommandName => "create_spatial_volume_overlay";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;
            var target = (p.Value<string>("target") ?? "room").ToLowerInvariant(); // "room" | "space"
            var viewId = p.Value<int?>("viewId");
            var heightMmNullable = p["heightMm"]?.ToObject<double?>();
            var colorObj = p["color"] as JObject;
            var transparency = Math.Max(0, Math.Min(100, p.Value<int?>("transparency") ?? 60));

            var r = (byte)(colorObj?["r"]?.Value<int?>() ?? 0);
            var g = (byte)(colorObj?["g"]?.Value<int?>() ?? 120);
            var b = (byte)(colorObj?["b"]?.Value<int?>() ?? 215);
            var col = new Color(r, g, b);

            View view = viewId.HasValue ? doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View
                                        : (uiapp.ActiveUIDocument?.ActiveGraphicalView as View) ?? doc.ActiveView;
            if (view == null) return Err("対象ビューが見つかりません。");

            // 入力要素ID解決
            var ids = new List<ElementId>();
            if (p["elementIds"] is JArray arr)
            {
                foreach (var it in arr) ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(it.Value<int>()));
            }
            else if (p["elementId"] != null)
            {
                ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId")));
            }
            else
            {
                return Err("elementIds（または elementId）が必要です。");
            }

            var created = new JArray();
            var skipped = new JArray();
            var errors = new JArray();

            // バッチ/時間スライス制御（長時間トランザクションの詰まり回避）
            int batchSize = Math.Max(10, Math.Min(2000, p.Value<int?>("batchSize") ?? 200));
            int maxMillisPerTx = Math.Max(500, Math.Min(15000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;
            if (startIndex >= ids.Count) startIndex = 0;
            var targets = ids.Skip(startIndex).Take(batchSize).ToList();
            int processed = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using (var t = new Transaction(doc, "Create Spatial Volume Overlay"))
            {
                t.Start();

                // ループ: 各 Room / Space
                foreach (var hostId in targets)
                {
                    try
                    {
                        var host = doc.GetElement(hostId);
                        if (host == null)
                        {
                            skipped.Add(Skip(hostId, "not found"));
                            continue;
                        }

                        // 1) 境界CurveLoop群の取得
                        var loops = GetBoundaryLoops(doc, host, target);
                        if (loops == null || loops.Count == 0)
                        {
                            skipped.Add(Skip(hostId, "no boundary"));
                            continue;
                        }

                        // 2) 高さの決定（mm→ft）
                        double heightMm = heightMmNullable ?? TryGetHeightFromElement(host) ?? 2500.0;
                        double heightFt = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);

                        // 3) Solid 作成（上向き押し出し）
                        //    CurveLoop は Revit 内部単位(ft)必須
                        //    ここでは既に ft 前提（Room/Space の Boundary はモデル単位＝ft）
                        //    ※もし mm ソースから作る場合は mm→ft 変換が必要
                        var solids = CreateExtrusionSolids(loops, XYZ.BasisZ, heightFt);
                        if (solids.Count == 0)
                        {
                            skipped.Add(Skip(hostId, "failed to build solid"));
                            continue;
                        }

                        // 4) DirectShape 作成（GenericModel）
                        var ds = DirectShape.CreateElement(doc, Autodesk.Revit.DB.ElementIdCompat.From(BuiltInCategory.OST_GenericModel));
                        ds.ApplicationId = "RevitMCP";
                        ds.ApplicationDataId = host.UniqueId ?? host.Id.IntValue().ToString();

                        var shape = new List<GeometryObject>(solids);
                        ds.SetShape(shape);

                        // 5) ビュー単位で色/透過を適用（Surface 前景パターンを Solid に）
                        var solidFill = GetSolidFillPatternElement(doc);
                        var ogs = new OverrideGraphicSettings();
                        if (solidFill != null)
                        {
                            ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                            ogs.SetSurfaceForegroundPatternColor(col);
                        }
                        ogs.SetSurfaceTransparency(transparency); // 0(不透明)〜100(完全透明)
                        view.SetElementOverrides(ds.Id, ogs);

                        created.Add(new JObject
                        {
                            ["hostId"] = hostId.IntValue(),
                            ["directShapeId"] = ds.Id.IntValue()
                        });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new JObject
                        {
                            ["hostId"] = hostId.IntValue(),
                            ["message"] = ex.Message
                        });
                    }
                }

                t.Commit();
            }

            if (refreshView)
            {
                try { doc.Regenerate(); } catch { }
                try { uidoc?.RefreshActiveView(); } catch { }
            }

            int nextIndex = startIndex + processed;
            bool completed = nextIndex >= ids.Count;

            var result = new JObject
            {
                ["ok"] = true,
                ["viewId"] = view.Id.IntValue(),
                ["created"] = created,
                ["skipped"] = skipped,
                ["errors"] = errors,
                ["completed"] = completed
            };
            if (!completed)
            {
                result["nextIndex"] = nextIndex;
                result["batchSize"] = batchSize;
            }
            return result;
        }

        // -------- helpers --------

        private static JObject Err(string msg) => new JObject { ["ok"] = false, ["msg"] = msg };

        private static JObject Skip(ElementId id, string reason)
            => new JObject { ["hostId"] = id.IntValue(), ["reason"] = reason };

        /// <summary>
        /// Room/Space の境界を CurveLoop 群で取得（内部単位 ft）
        /// </summary>
        private static List<CurveLoop> GetBoundaryLoops(Document doc, Element host, string target)
        {
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            var loops = new List<CurveLoop>();

            if (target == "room" && host is Autodesk.Revit.DB.Architecture.Room room)
            {
                foreach (var segs in room.GetBoundarySegments(opts) ?? new List<IList<BoundarySegment>>())
                    loops.Add(ToCurveLoop(segs));
            }
            else if (target == "space" && host is Autodesk.Revit.DB.Mechanical.Space space)
            {
                foreach (var segs in space.GetBoundarySegments(opts) ?? new List<IList<BoundarySegment>>())
                    loops.Add(ToCurveLoop(segs));
            }
            else
            {
                // 型が合わない場合は null 返却
                return new List<CurveLoop>();
            }
            // 無効 loop を除去
            loops = loops.Where(cl => cl != null && cl.IsCounterclockwise(XYZ.BasisZ)).ToList();
            return loops;
        }

        private static CurveLoop ToCurveLoop(IList<BoundarySegment> segs)
        {
            var cl = new CurveLoop();
            foreach (var s in segs)
            {
                var c = s?.GetCurve();
                if (c == null) continue;
                // 可能ならトリムして継ぎ目を綺麗に
                cl.Append(c);
            }
            return cl;
        }

        /// <summary>
        /// Unbounded Height 等を試す（mm）。取得できなければ null。
        /// </summary>
        private static double? TryGetHeightFromElement(Element e)
        {
            // Room: Unbounded Height or "高さ"系
            var p1 = e.get_Parameter(BuiltInParameter.ROOM_HEIGHT); // Unbounded Height（環境により）
            if (p1 != null && p1.StorageType == StorageType.Double)
                return UnitUtils.ConvertFromInternalUnits(p1.AsDouble(), UnitTypeId.Millimeters);

            var p2 = e.LookupParameter("Unbounded Height");
            if (p2 != null && p2.StorageType == StorageType.Double)
                return UnitUtils.ConvertFromInternalUnits(p2.AsDouble(), UnitTypeId.Millimeters);

            // Space: Limit Offset + Upper Limit などから導くのが理想だが、簡易に試行
            var p3 = e.LookupParameter("Height");
            if (p3 != null && p3.StorageType == StorageType.Double)
                return UnitUtils.ConvertFromInternalUnits(p3.AsDouble(), UnitTypeId.Millimeters);

            return null;
        }

        private static List<Solid> CreateExtrusionSolids(List<CurveLoop> loops, XYZ dir, double height)
        {
            var solids = new List<Solid>();
            foreach (var cl in loops)
            {
                if (cl == null) continue;
                try
                {
                    // Revitは外周＋内周の複合に対応：外周を最初、内周は逆向き（CCW/CW）で与えるのが吉
                    // ここでは単純に個別Extrusion（必要なら面の差し引きに拡張）
                    var s = GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { cl }, dir, height);
                    if (s != null) solids.Add(s);
                }
                catch { /* 無効ループは捨てる */ }
            }
            return solids;
        }

        private static FillPatternElement? GetSolidFillPatternElement(Document doc)
        {
            try
            {
                // Drafting用を優先、なければModel用を探す
                return FillPatternElement.GetFillPatternElementByName(
                           doc, FillPatternTarget.Drafting, "Solid fill")
                       ?? FillPatternElement.GetFillPatternElementByName(
                           doc, FillPatternTarget.Model, "Solid fill");
            }
            catch
            {
                return null;
            }
        }
    }
}


