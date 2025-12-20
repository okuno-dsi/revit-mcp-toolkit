// File: RevitMCPAddin/Commands/ElementOps/Ceiling/GetCeilingBoundariesCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    /// <summary>
    /// 天井(Ceiling)の水平面から境界ループを抽出し、mm座標・面積(mm2)・周長(mm)を返す。
    /// オプション:
    ///  - elementId / uniqueId : どちらでも指定可
    ///  - faceSelection : "bottom"(既定) | "largestHorizontal"
    ///  - flattenZ : true(既定) で全点のZ = レベル標高+オフセットに揃える
    ///  - includeEdges : true で Line/Arc のエッジ詳細を付加
    ///  - decimals : 小数桁(既定 3)
    /// </summary>
    public class GetCeilingBoundariesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_ceiling_boundaries";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            // 1) 対象解決 elementId / uniqueId
            Element elem = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) elem = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) elem = doc.GetElement(uid);

            var ceiling = elem as Autodesk.Revit.DB.Ceiling;
            if (ceiling == null) return new { ok = false, msg = "Ceiling 要素が見つかりません（elementId/uniqueIdを確認）。" };

            // 2) オプション
            string faceSel = (p.Value<string>("faceSelection") ?? "bottom").ToLowerInvariant(); // bottom|largestHorizontal
            bool flattenZ = p.Value<bool?>("flattenZ") ?? true;
            bool includeEdges = p.Value<bool?>("includeEdges") ?? false;
            int decimals = p.Value<int?>("decimals") ?? 3;

            // 3) レベル標高+オフセットから z を決定（flattenZ用）
            double zFlatMm = ComputeCeilingElevationMm(doc, ceiling);

            // 4) 水平 PlanarFace の抽出（UnitHelperは使わず、コマンド内で解決）
            var pf = TryGetPlanarFace(ceiling, faceSel);
            if (pf == null) return new { ok = false, msg = "水平な PlanarFace を特定できませんでした。" };

            // 5) ループ取得（コマンド内ヘルパー）
            var loops = SafeGetLoops(pf);
            if (loops == null || loops.Count == 0) return new { ok = false, msg = "境界ループが取得できませんでした。" };

            // 6) ループごとの面積・周長を計算（ft→mm2/mm）
            var loopsInfo = new List<(CurveLoop loop, double areaMm2, double perimMm)>(loops.Count);
            foreach (var loop in loops)
            {
                ComputeAreaPerimeter(loop, pf, out double areaFt2, out double perimFt);
                loopsInfo.Add((loop,
                    Math.Round(ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMillimeters), decimals),
                    Math.Round(ConvertFromInternalUnits(perimFt, UnitTypeId.Millimeters), decimals)));
            }

            // 7) 外周/内周判別：面積の絶対値が最大＝外周、他は内周
            loopsInfo = loopsInfo
                .OrderByDescending(t => Math.Abs(t.areaMm2))
                .ToList();

            // 8) 出力整形
            var boundaries = new List<object>(loopsInfo.Count);
            for (int i = 0; i < loopsInfo.Count; i++)
            {
                var t = loopsInfo[i];
                string role = (i == 0) ? "outer" : "inner";

                // 点群（mm）
                var pts = new List<object>();
                foreach (var crv in t.loop)
                {
                    var ep0 = crv.GetEndPoint(0);
                    double zx = flattenZ ? zFlatMm : ConvertFromInternalUnits(ep0.Z, UnitTypeId.Millimeters);
                    pts.Add(new
                    {
                        x = Math.Round(ConvertFromInternalUnits(ep0.X, UnitTypeId.Millimeters), decimals),
                        y = Math.Round(ConvertFromInternalUnits(ep0.Y, UnitTypeId.Millimeters), decimals),
                        z = Math.Round(zx, decimals)
                    });
                }

                // エッジ詳細（任意）
                List<object> edges = null;
                if (includeEdges)
                {
                    edges = new List<object>();
                    foreach (var crv in t.loop)
                    {
                        if (crv is Line ln)
                        {
                            var s = ln.GetEndPoint(0);
                            var e = ln.GetEndPoint(1);
                            edges.Add(new
                            {
                                kind = "line",
                                start = ToPtMm(s, flattenZ ? zFlatMm : (double?)null, decimals),
                                end = ToPtMm(e, flattenZ ? zFlatMm : (double?)null, decimals),
                                lengthMm = Math.Round(ConvertFromInternalUnits(ln.ApproximateLength, UnitTypeId.Millimeters), decimals)
                            });
                        }
                        else if (crv is Arc arc)
                        {
                            var s = arc.GetEndPoint(0);
                            var e = arc.GetEndPoint(1);
                            var m = arc.Evaluate(0.5, true);
                            edges.Add(new
                            {
                                kind = "arc",
                                start = ToPtMm(s, flattenZ ? zFlatMm : (double?)null, decimals),
                                mid = ToPtMm(m, flattenZ ? zFlatMm : (double?)null, decimals),
                                end = ToPtMm(e, flattenZ ? zFlatMm : (double?)null, decimals),
                                radiusMm = Math.Round(ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters), decimals),
                                lengthMm = Math.Round(ConvertFromInternalUnits(arc.ApproximateLength, UnitTypeId.Millimeters), decimals)
                            });
                        }
                        else
                        {
                            var tess = crv.Tessellate();
                            if (tess != null && tess.Count > 1)
                            {
                                edges.Add(new
                                {
                                    kind = "poly",
                                    points = tess.Select(pt => ToPtMm(pt, flattenZ ? zFlatMm : (double?)null, decimals)).ToList(),
                                    lengthMm = Math.Round(ConvertFromInternalUnits(crv.ApproximateLength, UnitTypeId.Millimeters), decimals)
                                });
                            }
                        }
                    }
                }

                boundaries.Add(new
                {
                    role,
                    areaMm2 = t.areaMm2,
                    perimeterMm = t.perimMm,
                    points = pts,
                    edges
                });
            }

            return new
            {
                ok = true,
                elementId = ceiling.Id.IntegerValue,
                uniqueId = ceiling.UniqueId,
                faceSelection = faceSel,
                totalLoops = boundaries.Count,
                boundaries,
                units = new { Length = "mm", Area = "mm2" }
            };
        }

        // ---------------- helpers （このコマンドに内包）----------------

        private static object ToPtMm(XYZ p, double? zOverrideMm, int decimals)
        {
            var zmm = zOverrideMm ?? ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters);
            return new
            {
                x = Math.Round(ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters), decimals),
                y = Math.Round(ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters), decimals),
                z = Math.Round(zmm, decimals)
            };
        }

        /// <summary>レベル標高 + CEILING_HEIGHTABOVELEVEL_PARAM の合算(mm)。失敗時は 0。</summary>
        private static double ComputeCeilingElevationMm(Document doc, Autodesk.Revit.DB.Ceiling c)
        {
            double baseMm = 0.0;
            try
            {
                var level = doc.GetElement(c.LevelId) as Level;
                baseMm = (level != null) ? ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters) : 0.0;
            }
            catch { /* ignore */ }

            try
            {
                var p = c.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.Double)
                    return baseMm + ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            }
            catch { /* ignore */ }

            return baseMm;
        }

        /// <summary>faceSelection に従って PlanarFace を返す。</summary>
        private static PlanarFace TryGetPlanarFace(Autodesk.Revit.DB.Ceiling c, string faceSel)
        {
            var opt = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var ge = c.get_Geometry(opt);
            var pfCandidates = new List<PlanarFace>();

            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Faces != null)
                {
                    foreach (Autodesk.Revit.DB.Face f in s.Faces)
                    {
                        if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.99)
                            pfCandidates.Add(pf); // ほぼ水平
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    var ig = gi.GetInstanceGeometry();
                    foreach (GeometryObject igo in ig)
                    {
                        if (igo is Solid solid2 && solid2.Faces != null)
                        {
                            foreach (Autodesk.Revit.DB.Face f in solid2.Faces)
                            {
                                if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.99)
                                    pfCandidates.Add(pf);
                            }
                        }
                    }
                }
            }

            if (pfCandidates.Count == 0) return null;

            if (faceSel == "largesthorizontal")
            {
                // 面積最大の水平面
                double maxArea = double.NegativeInfinity;
                PlanarFace best = null;
                foreach (var pf in pfCandidates)
                {
                    double a = 0; try { a = pf.Area; } catch { a = 0; }
                    if (a > maxArea) { maxArea = a; best = pf; }
                }
                return best ?? pfCandidates[0];
            }
            else
            {
                // bottom: 下向き(Z<0)優先。なければ最大面
                var bottoms = pfCandidates.Where(x => x.FaceNormal.Z < 0).ToList();
                if (bottoms.Count > 0) return bottoms.OrderByDescending(SafeArea).First();
                return pfCandidates.OrderByDescending(SafeArea).First();
            }
        }

        private static double SafeArea(PlanarFace pf)
        {
            try { return pf.Area; } catch { return 0; }
        }

        /// <summary>PlanarFace から曲線ループ群を安全に取得。</summary>
        private static IList<CurveLoop> SafeGetLoops(PlanarFace pf)
        {
            try
            {
                var loops = pf.GetEdgesAsCurveLoops();
                return (loops == null || loops.Count == 0) ? new List<CurveLoop>() : loops;
            }
            catch
            {
                return new List<CurveLoop>();
            }
        }

        /// <summary>CurveLoop の面積(ft^2)と周長(ft)をテッセレーションで概算。</summary>
        private static void ComputeAreaPerimeter(CurveLoop loop, PlanarFace pf, out double areaFt2, out double perimFt)
        {
            var pts = new List<XYZ>();
            foreach (var crv in loop)
            {
                var tess = crv.Tessellate();
                if (tess != null && tess.Count > 0)
                {
                    if (pts.Count > 0 && pts[pts.Count - 1].IsAlmostEqualTo(tess[0]) && tess.Count > 1)
                        pts.AddRange(tess.Skip(1));
                    else
                        pts.AddRange(tess);
                }
            }

            // 周長(ft)
            perimFt = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                perimFt += a.DistanceTo(b);
            }

            // PlanarFace座標系に投影→Shoelaceで面積(ft^2)
            var o = pf.Origin;
            var xAxis = pf.XVector;
            var yAxis = pf.YVector;
            var poly2 = pts.Select(p =>
            {
                var v = p - o;
                return new XYZ(v.DotProduct(xAxis), v.DotProduct(yAxis), 0);
            }).ToList();

            double area2D = 0.0;
            for (int i = 0; i < poly2.Count; i++)
            {
                var a = poly2[i];
                var b = poly2[(i + 1) % poly2.Count];
                area2D += (a.X * b.Y - a.Y * b.X);
            }
            areaFt2 = Math.Abs(area2D) * 0.5;
        }
    }
}
