// RevitMCPAddin/Commands/ElementOps/FloorOps/GetFloorBoundaryCommand.cs
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class GetFloorBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // target
            Element elem = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) elem = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) elem = doc.GetElement(uid);

            var floor = elem as Autodesk.Revit.DB.Floor;
            if (floor == null) return new { ok = false, msg = "Floor 要素が見つかりません（elementId/uniqueId）。" };

            string faceSel = (p.Value<string>("faceSelection") ?? "top").Trim().ToLowerInvariant();
            bool flattenZ = p.Value<bool?>("flattenZ") ?? true;
            bool includeCurveType = p.Value<bool?>("includeCurveType") ?? true;
            bool includeLength = p.Value<bool?>("includeLength") ?? true;
            int decimals = p.Value<int?>("decimals") ?? 3;

            // 標高(mm) = レベル標高 + オフセット
            double zFlatMm = 0.0;
            try
            {
                var level = doc.GetElement(floor.LevelId) as Level;
                zFlatMm = UnitHelper.InternalToMm(level?.Elevation ?? 0);
                var off = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (off != null && off.StorageType == StorageType.Double)
                    zFlatMm += UnitHelper.InternalToMm(off.AsDouble());
            }
            catch { /* ignore */ }

            // 水平 PlanarFace
            var pf = TryGetPlanarFace(floor, faceSel);
            if (pf == null) return new { ok = false, msg = "水平な PlanarFace を特定できませんでした。" };

            var loops = SafeGetLoops(pf);
            if (loops == null || loops.Count == 0) return new { ok = false, msg = "境界ループが取得できませんでした。" };

            var ordered = loops.Select(loop => new { loop, area = SafeAreaByTessellate(loop, pf) })
                               .OrderByDescending(x => Math.Abs(x.area))
                               .ToList();

            var outLoops = new List<object>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var cl = ordered[i].loop;
                string role = (i == 0) ? "outer" : "inner";

                var segments = new List<object>();
                foreach (var crv in cl)
                {
                    var p0 = crv.GetEndPoint(0);
                    var p1 = crv.GetEndPoint(1);

                    var sMm = UnitHelper.XyzToMm(p0);
                    var eMm = UnitHelper.XyzToMm(p1);
                    double z0 = flattenZ ? zFlatMm : sMm.z;
                    double z1 = flattenZ ? zFlatMm : eMm.z;

                    var segObj = new Dictionary<string, object>
                    {
                        ["start"] = new { x = Math.Round(sMm.x, decimals), y = Math.Round(sMm.y, decimals), z = Math.Round(z0, decimals) },
                        ["end"] = new { x = Math.Round(eMm.x, decimals), y = Math.Round(eMm.y, decimals), z = Math.Round(z1, decimals) }
                    };

                    if (includeCurveType)
                    {
                        segObj["curveType"] = CurveKind(crv);

                        if (crv is Arc arc)
                        {
                            var cMm = UnitHelper.XyzToMm(arc.Center);
                            segObj["center"] = new { x = Math.Round(cMm.x, decimals), y = Math.Round(cMm.y, decimals), z = Math.Round(flattenZ ? zFlatMm : cMm.z, decimals) };
                            segObj["radiusMm"] = Math.Round(UnitHelper.FtToMm(arc.Radius), decimals);

                            if (includeLength)
                            {
                                segObj["lengthMm"] = Math.Round(UnitHelper.FtToMm(arc.ApproximateLength), decimals);
                            }
                        }
                    }

                    if (includeLength && !(crv is Arc))
                    {
                        segObj["lengthMm"] = Math.Round(UnitHelper.FtToMm(crv.ApproximateLength), decimals);
                    }

                    segments.Add(segObj);
                }

                outLoops.Add(new { loopIndex = i, role, segments });
            }

            return new
            {
                ok = true,
                elementId = floor.Id.IntegerValue,
                uniqueId = floor.UniqueId,
                faceSelection = faceSel,
                totalCount = outLoops.Count,
                loops = outLoops,
                units = new { Length = "mm" }
            };
        }

        private static string CurveKind(Curve c)
        {
            if (c is Line) return "Line";
            if (c is Arc) return "Arc";
            if (c is Ellipse) return "Ellipse";
            if (c is NurbSpline) return "Spline";
            return c?.GetType()?.Name ?? "Curve";
        }

        private static PlanarFace TryGetPlanarFace(Autodesk.Revit.DB.Floor f, string sel)
        {
            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
            var ge = f.get_Geometry(opt);
            var list = new List<PlanarFace>();

            void Collect(GeometryElement gelem)
            {
                if (gelem == null) return;
                foreach (var go in gelem)
                {
                    if (go is Solid s && s.Faces != null)
                        foreach (Autodesk.Revit.DB.Face face in s.Faces)
                            if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.99) list.Add(pf);
                            else if (go is GeometryInstance gi) Collect(gi.GetInstanceGeometry());
                }
            }
            Collect(ge);

            if (list.Count == 0) return null;

            if (sel == "top")
            {
                var tops = list.Where(x => x.FaceNormal.Z > 0).ToList();
                if (tops.Count > 0) return tops.OrderByDescending(x => SafeArea(x)).First();
            }
            else if (sel == "bottom")
            {
                var bots = list.Where(x => x.FaceNormal.Z < 0).ToList();
                if (bots.Count > 0) return bots.OrderByDescending(x => SafeArea(x)).First();
            }
            return list.OrderByDescending(x => SafeArea(x)).First();
        }

        private static double SafeArea(PlanarFace pf) { try { return pf.Area; } catch { return 0.0; } }

        private static IList<CurveLoop> SafeGetLoops(PlanarFace pf)
        {
            try { var a = pf.GetEdgesAsCurveLoops(); if (a != null && a.Count > 0) return a; } catch { }
            var result = new List<CurveLoop>();
            try
            {
                foreach (EdgeArray ea in pf.EdgeLoops)
                {
                    var cl = new CurveLoop();
                    foreach (Edge e in ea) { var c = e.AsCurve(); if (c != null) cl.Append(c); }
                    if (cl.Count() > 0) result.Add(cl);
                }
            }
            catch { }
            return result;
        }

        /// <summary>CurveLoop をテッセレート→靴紐公式（ft²）</summary>
        private static double SafeAreaByTessellate(CurveLoop loop, PlanarFace pf)
        {
            try
            {
                var pts = new List<XYZ>();
                foreach (var crv in loop)
                {
                    var ts = crv.Tessellate();
                    if (ts == null || ts.Count == 0) continue;
                    if (pts.Count > 0 && pts[pts.Count - 1].IsAlmostEqualTo(ts[0]) && ts.Count > 1)
                        pts.AddRange(ts.Skip(1));
                    else
                        pts.AddRange(ts);
                }
                if (pts.Count < 3) return 0.0;

                var o = pf.Origin; var x = pf.XVector; var y = pf.YVector;
                var poly = pts.Select(p => { var v = p - o; return new XYZ(v.DotProduct(x), v.DotProduct(y), 0); }).ToList();

                double area2d = 0.0;
                for (int i = 0; i < poly.Count; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                    area2d += (a.X * b.Y - a.Y * b.X);
                }
                return Math.Abs(area2d) * 0.5;
            }
            catch { return 0.0; }
        }
    }
}
