// ================================================================
// File: Commands/Space/SpaceInfoCommands.cs (UnitHelper完全統一版)
// Revit 2023 / .NET Framework 4.8 / C# 8
// 目的: Spaceの境界・壁一覧・セントロイド・メトリクス・形状取得を提供
// ポイント:
//  - すべての単位変換は UnitHelper 経由（mm, m2, m3, deg）
//  - エラーメッセージは { ok:false, msg } で統一
// 依存: Autodesk.Revit.DB, Autodesk.Revit.DB.Mechanical, Newtonsoft.Json.Linq, RevitMCPAddin.Core
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Space
{
    internal static class SpaceUtil
    {
        public static SpatialElementBoundaryLocation ParseBoundaryLocation(string s)
        {
            return SpatialUtils.ParseBoundaryLocation(s);
        }

        public static (double x, double y, double z) ToMm(XYZ p, Document doc)
            => (Math.Round(UnitHelper.InternalToMm(p.X, doc), 3),
                Math.Round(UnitHelper.InternalToMm(p.Y, doc), 3),
                Math.Round(UnitHelper.InternalToMm(p.Z, doc), 3));
    }

    // 1) 境界ループ取得（mm座標・セグメント情報・境界要素情報オプション）
    public class GetSpaceBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };
                var p = (JObject)(cmd.Params ?? new JObject());

                if (!p.TryGetValue("elementId", out var idToken))
                    return new { ok = false, msg = "Parameter 'elementId' is required." };
                int spaceId = idToken.Value<int>();

                bool includeElementInfo = p.Value<bool?>("includeElementInfo") ?? false;
                string boundaryLocation = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish"; // Finish(default) / Center / CoreCenter / CoreBoundary

                var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(spaceId)) as Autodesk.Revit.DB.Mechanical.Space;
                if (space == null) return new { ok = false, msg = $"Space not found: {spaceId}" };

                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpaceUtil.ParseBoundaryLocation(boundaryLocation)
                };

                var loops = space.GetBoundarySegments(opts);
                if (loops == null) return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), loops = new object[0], units = UnitHelper.DefaultUnitsMeta() };

                var result = loops.Select((loop, i) =>
                {
                    var segments = loop.Select(bs =>
                    {
                        var c = bs.GetCurve();
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        var s0 = SpaceUtil.ToMm(p0, doc);
                        var s1 = SpaceUtil.ToMm(p1, doc);

                        var seg = new
                        {
                            start = new { x = s0.x, y = s0.y, z = s0.z },
                            end = new { x = s1.x, y = s1.y, z = s1.z },
                            curveType = c.GetType().Name,
                            lengthMm = Math.Round(UnitHelper.InternalToMm(c.Length, doc), 3)
                        };

                        if (includeElementInfo)
                        {
                            var be = doc.GetElement(bs.ElementId);
                            return new
                            {
                                seg.start,
                                seg.end,
                                seg.curveType,
                                seg.lengthMm,
                                boundaryElement = new
                                {
                                    elementId = bs.ElementId.IntValue(),
                                    category = be?.Category?.Name ?? string.Empty,
                                    name = be?.Name ?? (be as ElementType)?.Name ?? string.Empty
                                }
                            };
                        }
                        return (object)seg;
                    }).ToList();

                    return new { loopIndex = i, segments = segments };
                }).ToList();

                return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), loops = result, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 2) 境界に接する壁ID一覧（重複排除）
    public class GetSpaceBoundaryWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_boundary_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };
                var p = (JObject)(cmd.Params ?? new JObject());

                if (!p.TryGetValue("elementId", out var idToken))
                    return new { ok = false, msg = "Parameter 'elementId' is required." };
                int spaceId = idToken.Value<int>();

                string boundaryLocation = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
                var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(spaceId)) as Autodesk.Revit.DB.Mechanical.Space;
                if (space == null) return new { ok = false, msg = $"Space not found: {spaceId}" };

                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpaceUtil.ParseBoundaryLocation(boundaryLocation)
                };

                var loops = space.GetBoundarySegments(opts);
                var wallIds = new List<int>();
                if (loops != null)
                {
                    foreach (var loop in loops)
                    {
                        foreach (var bs in loop)
                        {
                            var e = doc.GetElement(bs.ElementId);
                            if (e is Wall)
                            {
                                int idv = e.Id.IntValue();
                                if (!wallIds.Contains(idv)) wallIds.Add(idv);
                            }
                        }
                    }
                }
                return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), count = wallIds.Count, wallIds = wallIds, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 3) セントロイド（mm）＋オプションでBBox返却
    public class GetSpaceCentroidCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_centroid";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };
                var p = (JObject)(cmd.Params ?? new JObject());

                if (!p.TryGetValue("elementId", out var idToken))
                    return new { ok = false, msg = "Parameter 'elementId' is required." };
                int spaceId = idToken.Value<int>();
                bool includeBoundingBox = p.Value<bool?>("includeBoundingBox") ?? false;

                var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(spaceId)) as Autodesk.Revit.DB.Mechanical.Space;
                if (space == null) return new { ok = false, msg = $"Space not found: {spaceId}" };

                var calc = new SpatialElementGeometryCalculator(doc);
                var res = calc.CalculateSpatialElementGeometry(space);
                var solid = res?.GetGeometry();
                if (solid == null) return new { ok = false, msg = "Geometry not available (space may be open or not enclosed)." };

                var c = solid.ComputeCentroid();
                var mm = SpaceUtil.ToMm(c, doc);

                object bboxObj = null;
                if (includeBoundingBox)
                {
                    var bb = space.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var mn = SpaceUtil.ToMm(bb.Min, doc);
                        var mx = SpaceUtil.ToMm(bb.Max, doc);
                        bboxObj = new
                        {
                            min = new { x = mn.x, y = mn.y, z = mn.z },
                            max = new { x = mx.x, y = mx.y, z = mx.z }
                        };
                    }
                }

                return new
                {
                    ok = true,
                    centroid = new { x = mm.x, y = mm.y, z = mm.z },
                    boundingBox = bboxObj
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 4) 面積/体積/外周長などのメトリクス（m²/m³/mm）
    public class GetSpaceMetricsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_metrics";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };
                var p = (JObject)(cmd.Params ?? new JObject());

                if (!p.TryGetValue("elementId", out var idToken))
                    return new { ok = false, msg = "Parameter 'elementId' is required." };
                int spaceId = idToken.Value<int>();

                string boundaryLocation = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";

                var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(spaceId)) as Autodesk.Revit.DB.Mechanical.Space;
                if (space == null) return new { ok = false, msg = $"Space not found: {spaceId}" };

                // Area / Volume（Revit設定によって0の場合あり）
                double areaM2 = Math.Round(UnitHelper.InternalToSqm(space.Area), 3);
                double volumeM3 = 0.0;
                try { volumeM3 = Math.Round(UnitHelper.InternalToCubicMeters(space.Volume), 3); } catch { /* volume未計算でも無視 */ }

                // Perimeter（境界曲線合計長）
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpaceUtil.ParseBoundaryLocation(boundaryLocation)
                };
                double perimeterMm = 0.0;
                var loops = space.GetBoundarySegments(opts);
                if (loops != null)
                {
                    foreach (var loop in loops)
                        foreach (var bs in loop)
                            perimeterMm += UnitHelper.InternalToMm(bs.GetCurve().Length, doc);
                }
                perimeterMm = Math.Round(perimeterMm, 3);

                string levelName = (doc.GetElement(space.LevelId) as Level)?.Name ?? string.Empty;

                return new
                {
                    ok = true,
                    elementId = spaceId,
                    areaM2,
                    volumeM3,
                    perimeterMm,
                    level = levelName,
                    boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(),
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 5) 形状セグメント（Line/Arc等、mm座標・長さ・Arcの中心/半径/角度等）
    public class GetSpaceGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_space_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };
                var p = (JObject)(cmd.Params ?? new JObject());

                if (!p.TryGetValue("elementId", out var idToken))
                    return new { ok = false, msg = "Parameter 'elementId' is required." };
                int spaceId = idToken.Value<int>();

                string boundaryLocation = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";

                var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(spaceId)) as Autodesk.Revit.DB.Mechanical.Space;
                if (space == null) return new { ok = false, msg = $"Space not found: {spaceId}" };

                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpaceUtil.ParseBoundaryLocation(boundaryLocation)
                };
                var loops = space.GetBoundarySegments(opts);

                if (loops == null) return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), geometry = new object[0], units = UnitHelper.DefaultUnitsMeta() };

                var geometry = loops.Select(loop =>
                {
                    var segs = new List<object>();
                    foreach (var bs in loop)
                    {
                        var c = bs.GetCurve();
                        var a0 = c.GetEndPoint(0);
                        var a1 = c.GetEndPoint(1);
                        var p0 = SpaceUtil.ToMm(a0, doc);
                        var p1 = SpaceUtil.ToMm(a1, doc);

                        if (c is Arc arc)
                        {
                            var center = SpaceUtil.ToMm(arc.Center, doc);
                            double radiusMm = Math.Round(UnitHelper.InternalToMm(arc.Radius, doc), 3);

                            // 角度：XY投影で近似
                            double startDeg = Math.Atan2(a0.Y - arc.Center.Y, a0.X - arc.Center.X) * 180.0 / Math.PI;
                            double endDeg = Math.Atan2(a1.Y - arc.Center.Y, a1.X - arc.Center.X) * 180.0 / Math.PI;

                            segs.Add(new
                            {
                                type = "Arc",
                                start = new { x = p0.x, y = p0.y, z = p0.z },
                                end = new { x = p1.x, y = p1.y, z = p1.z },
                                center = new { x = center.x, y = center.y, z = center.z },
                                radiusMm,
                                startAngleDeg = Math.Round(startDeg, 6),
                                endAngleDeg = Math.Round(endDeg, 6),
                                lengthMm = Math.Round(UnitHelper.InternalToMm(c.Length, doc), 3)
                            });
                        }
                        else
                        {
                            segs.Add(new
                            {
                                type = c.GetType().Name,
                                start = new { x = p0.x, y = p0.y, z = p0.z },
                                end = new { x = p1.x, y = p1.y, z = p1.z },
                                lengthMm = Math.Round(UnitHelper.InternalToMm(c.Length, doc), 3)
                            });
                        }
                    }
                    return segs;
                }).ToList();

                return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), geometry = geometry, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


