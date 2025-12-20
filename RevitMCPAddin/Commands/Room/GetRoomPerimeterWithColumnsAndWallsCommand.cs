// ======================================================================
// File   : Commands/Room/GetRoomPerimeterWithColumnsAndWallsCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: get_room_perimeter_with_columns_and_walls
// Purpose:
//   Compute room perimeter with temporary column room-bounding, and
//   additionally match nearby walls (Basic, Curtain, Stacked) to the
//   room boundary segments using 2D geometry analysis.
// ======================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomPerimeterWithColumnsAndWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_perimeter_with_columns_and_walls";

        private class BoundarySegmentInfo
        {
            public int LoopIndex { get; }
            public int SegmentIndex { get; }
            public GeometryUtils.Segment2 Seg2 { get; }

            public BoundarySegmentInfo(int loopIndex, int segmentIndex, GeometryUtils.Segment2 seg2)
            {
                LoopIndex = loopIndex;
                SegmentIndex = segmentIndex;
                Seg2 = seg2;
            }
        }

        private class WallMatch
        {
            public int WallId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public int TypeId { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public List<(int loopIndex, int segmentIndex)> Segments { get; } =
                new List<(int loopIndex, int segmentIndex)>();
            public double MinDistanceMm { get; set; } = double.MaxValue;
            public double MaxOverlapMm { get; set; } = 0.0;
            public XYZ StartFt { get; set; } = XYZ.Zero;
            public XYZ EndFt { get; set; } = XYZ.Zero;
        }

        private class WallMatchResult
        {
            public object[] Walls { get; set; } = Array.Empty<object>();
            public object? Debug { get; set; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("roomId", out var roomToken))
                return ResultUtil.Err("roomId is required.");

            int roomId = roomToken.Value<int>();
            var room = doc.GetElement(new ElementId(roomId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null)
                return ResultUtil.Err($"Room not found: roomId={roomId}");

            // Options (original behavior)
            bool includeSegments = p.Value<bool?>("includeSegments") ?? false;
            bool includeIslands = p.Value<bool?>("includeIslands") ?? true;
            string? boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            bool useBuiltInPerimeterIfAvailable = p.Value<bool?>("useBuiltInPerimeterIfAvailable") ?? true;

            // New wall-matching options
            bool includeWallMatches = p.Value<bool?>("includeWallMatches") ?? true;
            double wallMaxOffsetMm = p.Value<double?>("wallMaxOffsetMm") ?? 300.0;
            double wallMinOverlapMm = p.Value<double?>("wallMinOverlapMm") ?? 100.0;
            double wallMaxAngleDeg = p.Value<double?>("wallMaxAngleDeg") ?? 3.0;
            double wallSearchMarginMm = p.Value<double?>("wallSearchMarginMm") ?? 5000.0;

            if (wallMaxOffsetMm < 0) wallMaxOffsetMm = 0;
            if (wallMinOverlapMm < 0) wallMinOverlapMm = 0;
            if (wallMaxAngleDeg < 0) wallMaxAngleDeg = 0;
            if (wallSearchMarginMm < 0) wallSearchMarginMm = 0;

            // Column IDs
            var columnIds = new List<ElementId>();
            if (p.TryGetValue("columnIds", out var colArrayToken) && colArrayToken is JArray colArray)
            {
                foreach (var t in colArray)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        int id = t.Value<int>();
                        if (id > 0) columnIds.Add(new ElementId(id));
                    }
                }
            }

            bool autoDetectColumns = p.Value<bool?>("autoDetectColumnsInRoom") ?? false;
            double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 1000.0;

            IList<ElementId> autoDetectedColumns = new List<ElementId>();
            if (autoDetectColumns)
            {
                try
                {
                    autoDetectedColumns = AutoDetectColumnsInRoom(doc, room, searchMarginMm);
                    if (autoDetectedColumns != null)
                    {
                        foreach (var eid in autoDetectedColumns)
                        {
                            if (!columnIds.Contains(eid))
                                columnIds.Add(eid);
                        }
                    }
                }
                catch
                {
                    autoDetectedColumns = new List<ElementId>();
                }
            }

            // boundaryLocation
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };

            double perimeterFeet = 0.0;
            var toggledColumnIds = new List<int>();

            List<object>? loopsOut = includeSegments ? new List<object>() : null;
            var boundarySegments = (includeSegments || includeWallMatches)
                ? new List<BoundarySegmentInfo>()
                : null;

            using (var tg = new TransactionGroup(doc, "Temp RoomBounding for Perimeter+Walls"))
            {
                tg.Start();

                // (1) Temporarily enable Room Bounding on columns
                if (columnIds.Count > 0)
                {
                    using (var t = new Transaction(doc, "Enable Room Bounding for columns"))
                    {
                        t.Start();
                        foreach (var id in columnIds)
                        {
                            var e = doc.GetElement(id);
                            if (e == null) continue;

                            var pRoomBound = e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (pRoomBound != null && !pRoomBound.IsReadOnly)
                            {
                                try
                                {
                                    pRoomBound.Set(1);
                                    toggledColumnIds.Add(id.IntegerValue);
                                }
                                catch
                                {
                                    // ignore
                                }
                            }
                        }
                        t.Commit();
                    }

                    try { doc.Regenerate(); } catch { /* ignore */ }
                }

                // (2) Try ROOM_PERIMETER
                if (useBuiltInPerimeterIfAvailable)
                {
                    var paramPerim = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
                    if (paramPerim != null && paramPerim.StorageType == StorageType.Double)
                    {
                        try { perimeterFeet = paramPerim.AsDouble(); }
                        catch { perimeterFeet = 0.0; }
                    }
                }

                // (3) Use Room.GetBoundarySegments when necessary
                if (!useBuiltInPerimeterIfAvailable || perimeterFeet <= 0.0 || includeSegments || includeWallMatches)
                {
                    var boundaryLoops = room.GetBoundarySegments(opt);
                    if (boundaryLoops != null)
                    {
                        int loopIndex = 0;
                        foreach (var loop in boundaryLoops)
                        {
                            if (!includeIslands && loopIndex > 0)
                                break;

                            List<object>? segObjs = includeSegments ? new List<object>() : null;
                            int segIndex = 0;

                            foreach (var bs in loop)
                            {
                                var c = bs.GetCurve();
                                if (c == null)
                                {
                                    segIndex++;
                                    continue;
                                }

                                if (!useBuiltInPerimeterIfAvailable || perimeterFeet <= 0.0)
                                    perimeterFeet += c.Length;

                                var p0 = c.GetEndPoint(0);
                                var p1 = c.GetEndPoint(1);

                                double x0mm = UnitHelper.FtToMm(p0.X);
                                double y0mm = UnitHelper.FtToMm(p0.Y);
                                double x1mm = UnitHelper.FtToMm(p1.X);
                                double y1mm = UnitHelper.FtToMm(p1.Y);

                                if (boundarySegments != null)
                                {
                                    var seg2 = new GeometryUtils.Segment2(
                                        new GeometryUtils.Vec2(x0mm, y0mm),
                                        new GeometryUtils.Vec2(x1mm, y1mm)
                                    );
                                    boundarySegments.Add(new BoundarySegmentInfo(loopIndex, segIndex, seg2));
                                }

                                if (includeSegments && segObjs != null)
                                {
                                    segObjs.Add(new
                                    {
                                        start = new
                                        {
                                            x = Math.Round(x0mm, 3),
                                            y = Math.Round(y0mm, 3),
                                            z = Math.Round(UnitHelper.FtToMm(p0.Z), 3)
                                        },
                                        end = new
                                        {
                                            x = Math.Round(x1mm, 3),
                                            y = Math.Round(y1mm, 3),
                                            z = Math.Round(UnitHelper.FtToMm(p1.Z), 3)
                                        }
                                    });
                                }

                                segIndex++;
                            }

                            if (includeSegments && segObjs != null)
                            {
                                loopsOut!.Add(new
                                {
                                    loopIndex,
                                    segments = segObjs
                                });
                            }

                            loopIndex++;
                        }
                    }
                }

                // (4) Roll back any temporary changes
                tg.RollBack();
            }

            double perimeterMm = UnitUtils.ConvertFromInternalUnits(perimeterFeet, UnitTypeId.Millimeters);

            // (5) Wall matching
            object? wallsOutObj = null;
            object? wallMatchDebug = null;
            if (includeWallMatches && boundarySegments != null && boundarySegments.Count > 0)
            {
                var wallResult = MatchWallsNearRoomBoundary(
                    doc,
                    room,
                    boundarySegments,
                    wallMaxOffsetMm,
                    wallMinOverlapMm,
                    wallMaxAngleDeg,
                    wallSearchMarginMm);

                wallsOutObj = wallResult.Walls;
                wallMatchDebug = wallResult.Debug;
            }

            return ResultUtil.Ok(new
            {
                roomId = roomId,
                perimeterMm,
                loops = loopsOut,
                units = new { Length = "mm" },
                basis = new
                {
                    includeIslands,
                    boundaryLocation = opt.SpatialElementBoundaryLocation.ToString(),
                    useBuiltInPerimeterIfAvailable,
                    autoDetectColumnsInRoom = autoDetectColumns,
                    searchMarginMm,
                    autoDetectedColumnIds = autoDetectedColumns.Select(x => x.IntegerValue).ToArray(),
                    toggledColumnIds = toggledColumnIds,
                    includeWallMatches,
                    wallMaxOffsetMm,
                    wallMinOverlapMm,
                    wallMaxAngleDeg,
                    wallSearchMarginMm
                },
                wallMatchDebug,
                walls = wallsOutObj
            });
        }

        // ----------------------------------------------------------
        // Wall matching around room boundary (XY 2D)
        // ----------------------------------------------------------
        private static WallMatchResult MatchWallsNearRoomBoundary(
            Document doc,
            Autodesk.Revit.DB.Architecture.Room room,
            List<BoundarySegmentInfo> boundarySegments,
            double maxOffsetMm,
            double minOverlapMm,
            double maxAngleDeg,
            double searchMarginMm)
        {
            var level = doc.GetElement(room.LevelId) as Level;
            double levelZFt = level?.Elevation ?? 0.0;
            double levelElevMm = UnitHelper.FtToMm(levelZFt);

            // Bounding box of boundary in mm (XY)
            double minXmm = double.MaxValue, minYmm = double.MaxValue;
            double maxXmm = double.MinValue, maxYmm = double.MinValue;

            foreach (var bs in boundarySegments)
            {
                var a = bs.Seg2.A;
                var b = bs.Seg2.B;
                minXmm = Math.Min(minXmm, Math.Min(a.X, b.X));
                minYmm = Math.Min(minYmm, Math.Min(a.Y, b.Y));
                maxXmm = Math.Max(maxXmm, Math.Max(a.X, b.X));
                maxYmm = Math.Max(maxYmm, Math.Max(a.Y, b.Y));
            }

            if (boundarySegments.Count == 0 || double.IsInfinity(minXmm))
                return new WallMatchResult
                {
                    Walls = Array.Empty<object>(),
                    Debug = new
                    {
                        candidateWallCount = 0,
                        matchedWallCount = 0,
                        levelId = room.LevelId.IntegerValue,
                        levelElevationMm = levelElevMm,
                        maxOffsetMm,
                        minOverlapMm,
                        maxAngleDeg,
                        searchMarginMm
                    }
                };

            // 1) Collect candidate walls by level (vertical span)
            var allWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements()
                .OfType<Autodesk.Revit.DB.Wall>()
                .ToList();

            var candidateWalls = new List<Autodesk.Revit.DB.Wall>();
            foreach (var w in allWalls)
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

            // 2) 2D search window around room perimeter (plan)
            if (searchMarginMm < 0) searchMarginMm = 0;

            double minSearchXmm = minXmm - searchMarginMm;
            double minSearchYmm = minYmm - searchMarginMm;
            double maxSearchXmm = maxXmm + searchMarginMm;
            double maxSearchYmm = maxYmm + searchMarginMm;

            bool InSearchBox(XYZ pFt)
            {
                double xMm = UnitHelper.FtToMm(pFt.X);
                double yMm = UnitHelper.FtToMm(pFt.Y);
                return xMm >= minSearchXmm && xMm <= maxSearchXmm &&
                       yMm >= minSearchYmm && yMm <= maxSearchYmm;
            }

            // トレランスはユーザー指定のしきい値に合わせる
            var tol = new GeometryUtils.Tolerance(
                distMm: maxOffsetMm,
                angleDeg: maxAngleDeg
            );

            var wallMatches = new Dictionary<int, WallMatch>();

            foreach (var wall in candidateWalls)
            {
                var locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;
                var curve = locCurve.Curve;
                if (curve == null) continue;

                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                // quick 2D bounding check in mm
                if (!InSearchBox(p0) && !InSearchBox(p1))
                    continue;

                double wx1mm = UnitHelper.FtToMm(p0.X);
                double wy1mm = UnitHelper.FtToMm(p0.Y);
                double wx2mm = UnitHelper.FtToMm(p1.X);
                double wy2mm = UnitHelper.FtToMm(p1.Y);

                var wSeg = new GeometryUtils.Segment2(
                    new GeometryUtils.Vec2(wx1mm, wy1mm),
                    new GeometryUtils.Vec2(wx2mm, wy2mm)
                );

                foreach (var bseg in boundarySegments)
                {
                    var line = GeometryUtils.AnalyzeSegments2D(bseg.Seg2, wSeg, tol);
                    if (!line.ok) continue;

                    // 平行判定: GeometryUtils の isParallel（angleDeg <= tol.AngleDeg）を使用
                    if (!line.isParallel && !line.isColinear) continue;

                    // 平行時の最短距離（mm）
                    double distMm;
                    if (line.distanceBetweenParallelMm.HasValue)
                    {
                        distMm = line.distanceBetweenParallelMm.Value;
                    }
                    else
                    {
                        // フォールバック: 点と直線距離から算出
                        var a = bseg.Seg2.A;
                        var b = bseg.Seg2.B;
                        var ws2d = new GeometryUtils.Vec2(wx1mm, wy1mm);
                        var dir = new GeometryUtils.Vec2(b.X - a.X, b.Y - a.Y);
                        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                        if (len <= 1e-6) continue;
                        var ux = dir.X / len;
                        var uy = dir.Y / len;
                        var vx = ws2d.X - a.X;
                        var vy = ws2d.Y - a.Y;
                        distMm = Math.Abs(vx * uy - vy * ux);
                    }
                    if (distMm < 0 || distMm > maxOffsetMm) continue;

                    // 外周線方向への投影重複長（mm）を自前で計算
                    {
                        var a = bseg.Seg2.A;
                        var b = bseg.Seg2.B;
                        double dx = b.X - a.X;
                        double dy = b.Y - a.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy);
                        if (len <= 1e-6) continue;
                        double ux = dx / len;
                        double uy = dy / len;

                        // 壁基準線の両端を境界線軸に射影
                        double s2a = (wx1mm - a.X) * ux + (wy1mm - a.Y) * uy;
                        double s2b = (wx2mm - a.X) * ux + (wy2mm - a.Y) * uy;
                        double wmin = Math.Min(s2a, s2b);
                        double wmax = Math.Max(s2a, s2b);

                        double omin = Math.Max(0.0, wmin);
                        double omax = Math.Min(len, wmax);
                        double overlapMm = omax - omin;
                        if (overlapMm <= 0.0 || overlapMm < minOverlapMm) continue;

                        // ここまで来たら、この壁はこの境界線分とマッチ

                        int wid = wall.Id.IntegerValue;
                        if (!wallMatches.TryGetValue(wid, out var match))
                        {
                            var wt = wall.WallType;
                            match = new WallMatch
                            {
                                WallId = wid,
                                UniqueId = wall.UniqueId,
                                TypeId = wt != null ? wt.Id.IntegerValue : 0,
                                TypeName = wt != null ? wt.Name ?? string.Empty : string.Empty,
                                StartFt = p0,
                                EndFt = p1,
                                MinDistanceMm = distMm,
                                MaxOverlapMm = overlapMm
                            };
                            match.Segments.Add((bseg.LoopIndex, bseg.SegmentIndex));
                            wallMatches.Add(wid, match);
                        }
                        else
                        {
                            match.Segments.Add((bseg.LoopIndex, bseg.SegmentIndex));
                            match.MinDistanceMm = Math.Min(match.MinDistanceMm, distMm);
                            match.MaxOverlapMm = Math.Max(match.MaxOverlapMm, overlapMm);
                        }
                    }

                }
            }

            var wallsOut = wallMatches.Values
                .Select(w =>
                {
                    double sxMm = UnitHelper.FtToMm(w.StartFt.X);
                    double syMm = UnitHelper.FtToMm(w.StartFt.Y);
                    double szMm = UnitHelper.FtToMm(w.StartFt.Z);
                    double exMm = UnitHelper.FtToMm(w.EndFt.X);
                    double eyMm = UnitHelper.FtToMm(w.EndFt.Y);
                    double ezMm = UnitHelper.FtToMm(w.EndFt.Z);

                    return new
                    {
                        wallId = w.WallId,
                        uniqueId = w.UniqueId,
                        typeId = w.TypeId,
                        typeName = w.TypeName,
                        minDistanceMm = w.MinDistanceMm,
                        maxOverlapMm = w.MaxOverlapMm,
                        baseline = new
                        {
                            start = new
                            {
                                x = Math.Round(sxMm, 3),
                                y = Math.Round(syMm, 3),
                                z = Math.Round(szMm, 3)
                            },
                            end = new
                            {
                                x = Math.Round(exMm, 3),
                                y = Math.Round(eyMm, 3),
                                z = Math.Round(ezMm, 3)
                            }
                        },
                        segments = w.Segments
                            .Select(s => new { loopIndex = s.loopIndex, segmentIndex = s.segmentIndex })
                            .ToArray()
                    };
                })
                .ToArray();

            return new WallMatchResult
            {
                Walls = wallsOut,
                Debug = new
                {
                    candidateWallCount = candidateWalls.Count,
                    matchedWallCount = wallsOut.Length,
                    levelId = room.LevelId.IntegerValue,
                    levelElevationMm = levelElevMm,
                    maxOffsetMm,
                    minOverlapMm,
                    maxAngleDeg,
                    searchMarginMm
                }
            };
        }

        // Column auto-detection helpers (reused from original command)
        private static IList<ElementId> AutoDetectColumnsInRoom(Document doc, Autodesk.Revit.DB.Architecture.Room room, double searchMarginMm)
        {
            var result = new List<ElementId>();
            if (doc == null || room == null) return result;

            var roomBb = room.get_BoundingBox(null);
            if (roomBb == null) return result;

            double marginFt = UnitUtils.ConvertToInternalUnits(searchMarginMm, UnitTypeId.Millimeters);

            var min = new XYZ(roomBb.Min.X - marginFt, roomBb.Min.Y - marginFt, roomBb.Min.Z - marginFt);
            var max = new XYZ(roomBb.Max.X + marginFt, roomBb.Max.Y + marginFt, roomBb.Max.Z + marginFt);
            var outline = new Outline(min, max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var filters = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Columns),
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns)
            };
            var catFilter = new LogicalOrFilter(filters);

            var collector = new FilteredElementCollector(doc)
                .WherePasses(catFilter)
                .WherePasses(bbFilter)
                .WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                var fi = e as FamilyInstance;
                if (fi == null) continue;
                var bb = fi.get_BoundingBox(null);
                if (bb == null) continue;

                if (IntersectsRoomApprox(room, bb, roomBb))
                    result.Add(fi.Id);
            }

            return result;
        }

        private static bool IntersectsRoomApprox(Autodesk.Revit.DB.Architecture.Room room, BoundingBoxXYZ colBb, BoundingBoxXYZ roomBb)
        {
            if (room == null || colBb == null || roomBb == null) return false;

            double zMin = Math.Max(colBb.Min.Z, roomBb.Min.Z);
            double zMax = Math.Min(colBb.Max.Z, roomBb.Max.Z);
            if (zMax <= zMin) return false;

            double zMid = 0.5 * (zMin + zMax);

            double xMin = colBb.Min.X, xMax = colBb.Max.X;
            double yMin = colBb.Min.Y, yMax = colBb.Max.Y;

            double xMid = 0.5 * (xMin + xMax);
            double yMid = 0.5 * (yMin + yMax);

            var pts = new[]
            {
                new XYZ(xMid, yMid, zMid),
                new XYZ(xMin, yMin, zMid),
                new XYZ(xMax, yMin, zMid),
                new XYZ(xMax, yMax, zMid),
                new XYZ(xMin, yMax, zMid)
            };

            foreach (var pt in pts)
            {
                try
                {
                    if (room.IsPointInRoom(pt)) return true;
                }
                catch
                {
                    // ignore
                }
            }

            return false;
        }
    }
}
