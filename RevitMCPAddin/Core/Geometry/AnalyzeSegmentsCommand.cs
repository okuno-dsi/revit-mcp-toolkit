// ================================================================
// File: RevitMCPAddin/Commands/Geometry/AnalyzeSegmentsCommand.cs
// Command: analyze_segments
// Desc   : 2D/3D 線分どうし + 点と線分 の幾何関係をまとめて解析
// Params : { mode: "2d"|"3d", seg1:{a:{x,y(,z)}, b:{...}}, seg2:{...},
//           point?: {x,y(,z)}, tol?:{distMm, angleDeg} }
// Return : { ok, mode, line, pointToSeg1?, pointToSeg2? }
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Geometry
{
    public class AnalyzeSegmentsCommand : IRevitCommandHandler
    {
        public string CommandName => "analyze_segments";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)cmd.Params;

                var mode = (p.Value<string>("mode") ?? "2d").ToLowerInvariant();
                var tolObj = p.Value<JObject>("tol");
                var tol = new GeometryUtils.Tolerance(
                    distMm: tolObj?.Value<double?>("distMm") ?? 0.1,
                    angleDeg: tolObj?.Value<double?>("angleDeg") ?? 1e-4
                );

                var s1 = ReadSegment(p.Value<JObject>("seg1"), mode);
                var s2 = ReadSegment(p.Value<JObject>("seg2"), mode);
                if (s1 == null || s2 == null)
                    return new { ok = false, msg = "seg1/seg2 の座標が不足しています。" };

                if (mode == "2d")
                {
                    var ss1 = s1.Value.seg2;
                    var ss2 = s2.Value.seg2;
                    var line = GeometryUtils.AnalyzeSegments2D(ss1, ss2, tol);

                    // 任意: point -> seg1/seg2
                    object? p1 = null, p2 = null;
                    var pointObj = p.Value<JObject>("point");
                    if (pointObj != null)
                    {
                        var pp = new GeometryUtils.Vec2(
                            pointObj.Value<double>("x"),
                            pointObj.Value<double>("y")
                        );
                        var r1 = GeometryUtils.AnalyzePointToSegment2D(pp, ss1, tol);
                        var r2 = GeometryUtils.AnalyzePointToSegment2D(pp, ss2, tol);
                        p1 = new
                        {
                            ok = r1.ok,
                            msg = r1.msg,
                            distanceMm = r1.distanceMm,
                            projection = new { x = r1.projection.x, y = r1.projection.y },
                            t = r1.t,
                            onSegment = r1.onSegment
                        };
                        p2 = new
                        {
                            ok = r2.ok,
                            msg = r2.msg,
                            distanceMm = r2.distanceMm,
                            projection = new { x = r2.projection.x, y = r2.projection.y },
                            t = r2.t,
                            onSegment = r2.onSegment
                        };
                    }

                    return new
                    {
                        ok = true,
                        mode = "2d",
                        line = new
                        {
                            ok = line.ok,
                            msg = line.msg,
                            isParallel = line.isParallel,
                            isColinear = line.isColinear,
                            angleDeg = line.angleDeg,
                            intersectionExists = line.intersectionExists,
                            intersection = line.intersection.HasValue ? new { x = line.intersection.Value.x, y = line.intersection.Value.y } : null,
                            distanceBetweenParallelMm = line.distanceBetweenParallelMm,
                            overlapExists = line.overlapExists,
                            overlapLengthMm = line.overlapLengthMm,
                            overlapStart = line.overlapStart.HasValue ? new { x = line.overlapStart.Value.x, y = line.overlapStart.Value.y } : null,
                            overlapEnd = line.overlapEnd.HasValue ? new { x = line.overlapEnd.Value.x, y = line.overlapEnd.Value.y } : null
                        },
                        pointToSeg1 = p1,
                        pointToSeg2 = p2
                    };
                }
                else if (mode == "3d")
                {
                    var ss1 = s1.Value.seg3;
                    var ss2 = s2.Value.seg3;
                    var line = GeometryUtils.AnalyzeSegments3D(ss1, ss2, tol);

                    object? p1 = null, p2 = null;
                    var pointObj = p.Value<JObject>("point");
                    if (pointObj != null)
                    {
                        var pp = new GeometryUtils.Vec3(
                            pointObj.Value<double>("x"),
                            pointObj.Value<double>("y"),
                            pointObj.Value<double>("z")
                        );
                        var r1 = GeometryUtils.AnalyzePointToSegment3D(pp, ss1, tol);
                        var r2 = GeometryUtils.AnalyzePointToSegment3D(pp, ss2, tol);
                        p1 = new
                        {
                            ok = r1.ok,
                            msg = r1.msg,
                            distanceMm = r1.distanceMm,
                            projection = new { x = r1.projection.x, y = r1.projection.y, z = r1.projection.z },
                            t = r1.t,
                            onSegment = r1.onSegment
                        };
                        p2 = new
                        {
                            ok = r2.ok,
                            msg = r2.msg,
                            distanceMm = r2.distanceMm,
                            projection = new { x = r2.projection.x, y = r2.projection.y, z = r2.projection.z },
                            t = r2.t,
                            onSegment = r2.onSegment
                        };
                    }

                    return new
                    {
                        ok = true,
                        mode = "3d",
                        line = new
                        {
                            ok = line.ok,
                            msg = line.msg,
                            isParallel = line.isParallel,
                            isColinear = line.isColinear,
                            angleDeg = line.angleDeg,
                            intersects = line.intersects,
                            intersection = line.intersection.HasValue ? new { x = line.intersection.Value.x, y = line.intersection.Value.y, z = line.intersection.Value.z } : null,
                            shortestDistanceMm = line.shortestDistanceMm,
                            closestOn1 = new { x = line.closestOn1.x, y = line.closestOn1.y, z = line.closestOn1.z },
                            closestOn2 = new { x = line.closestOn2.x, y = line.closestOn2.y, z = line.closestOn2.z }
                        },
                        pointToSeg1 = p1,
                        pointToSeg2 = p2
                    };
                }
                else
                {
                    return new { ok = false, msg = $"mode '{mode}' は不正です。'2d' か '3d' を指定してください。" };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static (GeometryUtils.Segment2 seg2, GeometryUtils.Segment3 seg3)? ReadSegment(JObject? obj, string mode)
        {
            if (obj == null) return null;
            var a = obj.Value<JObject>("a");
            var b = obj.Value<JObject>("b");
            if (a == null || b == null) return null;

            if (mode == "2d")
            {
                var s2 = new GeometryUtils.Segment2(
                    new GeometryUtils.Vec2(a.Value<double>("x"), a.Value<double>("y")),
                    new GeometryUtils.Vec2(b.Value<double>("x"), b.Value<double>("y"))
                );
                // ダミー3D（未使用）
                var s3 = new GeometryUtils.Segment3(
                    new GeometryUtils.Vec3(a.Value<double?>("x") ?? 0, a.Value<double?>("y") ?? 0, a.Value<double?>("z") ?? 0),
                    new GeometryUtils.Vec3(b.Value<double?>("x") ?? 0, b.Value<double?>("y") ?? 0, b.Value<double?>("z") ?? 0)
                );
                return (s2, s3);
            }
            else
            {
                var s3 = new GeometryUtils.Segment3(
                    new GeometryUtils.Vec3(a.Value<double>("x"), a.Value<double>("y"), a.Value<double>("z")),
                    new GeometryUtils.Vec3(b.Value<double>("x"), b.Value<double>("y"), b.Value<double>("z"))
                );
                // 2DはXY投影
                var s2 = new GeometryUtils.Segment2(
                    new GeometryUtils.Vec2(a.Value<double>("x"), a.Value<double>("y")),
                    new GeometryUtils.Vec2(b.Value<double>("x"), b.Value<double>("y"))
                );
                return (s2, s3);
            }
        }
    }
}
