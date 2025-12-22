// ================================================================
// File   : Commands/ElementOps/Wall/FindWallsNearSegmentsCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: find_walls_near_segments
// Purpose:
//   Given a Level and a set of 2D boundary segments (mm), find walls
//   (Basic, Curtain, Stacked) whose LocationCurve is almost parallel,
//   within a certain offset, and overlapping at least a minimum length.
//   Geometry is evaluated in plan (XY) using mm units.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class FindWallsNearSegmentsCommand : IRevitCommandHandler
    {
        public string CommandName => "find_walls_near_segments";

        private class BoundarySegment
        {
            public int Index { get; }
            public GeometryUtils.Segment2 Seg2 { get; }

            public BoundarySegment(int index, GeometryUtils.Segment2 seg2)
            {
                Index = index;
                Seg2 = seg2;
            }
        }

        private class WallMatch
        {
            public int WallId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public List<int> SegmentIndices { get; } = new List<int>();
            public double MinDistanceMm { get; set; } = double.MaxValue;
            public double MaxOverlapMm { get; set; } = 0.0;
            public XYZ StartFt { get; set; } = XYZ.Zero;
            public XYZ EndFt { get; set; } = XYZ.Zero;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---- levelId (required) ----
            if (!p.TryGetValue("levelId", out var levelTok))
                return ResultUtil.Err("levelId is required.");

            int levelId = levelTok.Value<int>();
            var level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level;
            if (level == null)
                return ResultUtil.Err($"Level not found: levelId={levelId}");

            // ---- segments (required) ----
            var segArray = p.Value<JArray>("segments");
            if (segArray == null || segArray.Count == 0)
                return ResultUtil.Err("segments array is required and must have at least one item.");

            // thresholds (mm / deg)
            double maxOffsetMm = p.Value<double?>("maxOffsetMm") ?? 300.0;
            double minOverlapMm = p.Value<double?>("minOverlapMm") ?? 100.0;
            double maxAngleDeg = p.Value<double?>("maxAngleDeg") ?? 3.0;
            double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 5000.0;

            if (maxOffsetMm < 0) maxOffsetMm = 0;
            if (minOverlapMm < 0) minOverlapMm = 0;
            if (maxAngleDeg < 0) maxAngleDeg = 0;
            if (searchMarginMm < 0) searchMarginMm = 0;

            var tol = new GeometryUtils.Tolerance(
                distMm: maxOffsetMm,
                angleDeg: maxAngleDeg
            );

            // ---- convert input segments (mm) to internal Segment2 ----
            var boundarySegs = new List<BoundarySegment>();
            for (int i = 0; i < segArray.Count; i++)
            {
                if (!(segArray[i] is JObject segObj)) continue;
                var s = segObj.Value<JObject>("start");
                var e = segObj.Value<JObject>("end");
                if (s == null || e == null) continue;

                var a = new GeometryUtils.Vec2(
                    s.Value<double>("x"),
                    s.Value<double>("y")
                );
                var b = new GeometryUtils.Vec2(
                    e.Value<double>("x"),
                    e.Value<double>("y")
                );

                var seg2 = new GeometryUtils.Segment2(a, b);
                boundarySegs.Add(new BoundarySegment(i, seg2));
            }

            if (boundarySegs.Count == 0)
                return ResultUtil.Err("segments did not contain valid start/end coordinates.");

            // ---- collect candidate walls on the given level ----
            // We collect all OST_Walls and then filter by vertical span intersecting the level elevation.
            double levelElevMm = UnitHelper.FtToMm(level.Elevation);

            var walls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements()
                .OfType<Autodesk.Revit.DB.Wall>()
                .ToList();

            var candidateWalls = new List<Autodesk.Revit.DB.Wall>();
            foreach (var w in walls)
            {
                try
                {
                    var baseLevelId = w.LevelId;
                    var baseLevel = baseLevelId != null && baseLevelId != ElementId.InvalidElementId
                        ? doc.GetElement(baseLevelId) as Level
                        : null;
                    if (baseLevel == null) continue;

                    double baseMm = UnitHelper.FtToMm(baseLevel.Elevation);

                    // Height: use WALL_USER_HEIGHT_PARAM (unconnected height) if positive,
                    // otherwise fall back to bounding box Z span.
                    double loMm = baseMm;
                    double hiMm = baseMm;

                    double heightFt = 0.0;
                    var hParam = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hParam != null && hParam.StorageType == StorageType.Double)
                    {
                        try { heightFt = hParam.AsDouble(); } catch { heightFt = 0.0; }
                    }

                    double heightMm = UnitHelper.FtToMm(heightFt);
                    if (heightMm > 1e-3)
                    {
                        hiMm = baseMm + heightMm;
                    }
                    else
                    {
                        var bb = w.get_BoundingBox(null);
                        if (bb != null)
                        {
                            double zMinMm = UnitHelper.FtToMm(bb.Min.Z);
                            double zMaxMm = UnitHelper.FtToMm(bb.Max.Z);
                            loMm = Math.Min(zMinMm, zMaxMm);
                            hiMm = Math.Max(zMinMm, zMaxMm);
                        }
                    }

                    double lo = Math.Min(loMm, hiMm);
                    double hi = Math.Max(loMm, hiMm);

                    if (lo - 1e-3 <= levelElevMm && levelElevMm <= hi + 1e-3)
                        candidateWalls.Add(w);
                }
                catch
                {
                    // skip problematic walls
                }
            }

            // ---- optional search margin (plan) to limit candidate walls ----
            // Build 2D bounding box of all segments
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var bs in boundarySegs)
            {
                var a = bs.Seg2.A;
                var b = bs.Seg2.B;
                minX = Math.Min(minX, Math.Min(a.X, b.X));
                minY = Math.Min(minY, Math.Min(a.Y, b.Y));
                maxX = Math.Max(maxX, Math.Max(a.X, b.X));
                maxY = Math.Max(maxY, Math.Max(a.Y, b.Y));
            }
            minX -= searchMarginMm; minY -= searchMarginMm;
            maxX += searchMarginMm; maxY += searchMarginMm;

            bool InSearchBox(XYZ pFt)
            {
                var (xMm, yMm, _) = UnitHelper.XyzToMm(pFt);
                return xMm >= minX && xMm <= maxX && yMm >= minY && yMm <= maxY;
            }

            // ---- match walls to segments ----
            var wallMatches = new Dictionary<int, WallMatch>();

            foreach (var wall in candidateWalls)
            {
                var loc = wall.Location as LocationCurve;
                var curve = loc?.Curve;
                if (curve == null) continue;

                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                // quick 2D bounding check
                if (!InSearchBox(p0) && !InSearchBox(p1))
                    continue;

                var sMm = UnitHelper.XyzToMm(p0);
                var eMm = UnitHelper.XyzToMm(p1);

                var wallSeg = new GeometryUtils.Segment2(
                    new GeometryUtils.Vec2(sMm.x, sMm.y),
                    new GeometryUtils.Vec2(eMm.x, eMm.y)
                );

                foreach (var bs in boundarySegs)
                {
                    var lineRes = GeometryUtils.AnalyzeSegments2D(bs.Seg2, wallSeg, tol);
                    if (!lineRes.ok) continue;
                    if (!lineRes.isParallel) continue;

                    if (!lineRes.distanceBetweenParallelMm.HasValue) continue;
                    double dist = lineRes.distanceBetweenParallelMm.Value;
                    if (dist < 0 || dist > maxOffsetMm) continue;

                    // Use overlapLengthMm if available; if not, approximate from overlapStart/End
                    double overlapMm = lineRes.overlapLengthMm ?? 0.0;
                    if (overlapMm <= 0 && lineRes.overlapStart.HasValue && lineRes.overlapEnd.HasValue)
                    {
                        var os = lineRes.overlapStart.Value;
                        var oe = lineRes.overlapEnd.Value;
                        overlapMm = Math.Sqrt(Math.Pow(oe.x - os.x, 2) + Math.Pow(oe.y - os.y, 2));
                    }
                    if (overlapMm <= 0 || overlapMm < minOverlapMm) continue;

                    int wid = wall.Id.IntValue();
                    if (!wallMatches.TryGetValue(wid, out var wm))
                    {
                        wm = new WallMatch
                        {
                            WallId = wid,
                            UniqueId = wall.UniqueId,
                            StartFt = p0,
                            EndFt = p1,
                            MinDistanceMm = dist,
                            MaxOverlapMm = overlapMm
                        };
                        wm.SegmentIndices.Add(bs.Index);
                        wallMatches.Add(wid, wm);
                    }
                    else
                    {
                        wm.SegmentIndices.Add(bs.Index);
                        if (dist < wm.MinDistanceMm) wm.MinDistanceMm = dist;
                        if (overlapMm > wm.MaxOverlapMm) wm.MaxOverlapMm = overlapMm;
                    }
                }
            }

            // ---- build result ----
            var wallsOut = wallMatches.Values
                .Select(wm =>
                {
                    var sMm = UnitHelper.XyzToMm(wm.StartFt);
                    var eMm = UnitHelper.XyzToMm(wm.EndFt);
                    return new
                    {
                        wallId = wm.WallId,
                        uniqueId = wm.UniqueId,
                        segmentIndices = wm.SegmentIndices.Distinct().OrderBy(x => x).ToArray(),
                        minDistanceMm = wm.MinDistanceMm,
                        maxOverlapMm = wm.MaxOverlapMm,
                        baseline = new
                        {
                            start = new { x = Math.Round(sMm.x, 3), y = Math.Round(sMm.y, 3), z = Math.Round(sMm.z, 3) },
                            end = new { x = Math.Round(eMm.x, 3), y = Math.Round(eMm.y, 3), z = Math.Round(eMm.z, 3) }
                        }
                    };
                })
                .OrderBy(x => x.wallId)
                .ToList();

            return ResultUtil.Ok(new
            {
                levelId,
                settings = new
                {
                    maxOffsetMm,
                    minOverlapMm,
                    maxAngleDeg,
                    searchMarginMm
                },
                wallCount = wallsOut.Count,
                walls = wallsOut
            });
        }
    }
}


