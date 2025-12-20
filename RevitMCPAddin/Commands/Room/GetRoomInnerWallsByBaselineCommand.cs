// ================================================================
// File   : Commands/Room/GetRoomInnerWallsByBaselineCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: get_room_inner_walls_by_baseline
// Purpose:
//   For a given Room, find walls (Basic/Curtain/Stacked) on the same
//   level (or spanning across it) whose baseline lies inside the room.
//   A wall is considered "inside" if the points at fractions t1 and t2
//   (defaults 0.33 and 0.67) along its LocationCurve are both inside
//   the room volume when tested at the room's mid-height.
// ================================================================
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
    public class GetRoomInnerWallsByBaselineCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_inner_walls_by_baseline";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("roomId", out var roomToken))
                return ResultUtil.Err("roomId is required.");

            int roomId = roomToken.Value<int>();
            var room = doc.GetElement(new ElementId(roomId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null)
                return ResultUtil.Err($"Room not found: roomId={roomId}");

            double t1 = p.Value<double?>("t1") ?? 0.33;
            double t2 = p.Value<double?>("t2") ?? 0.67;
            double? searchRadiusMm = p.Value<double?>("searchRadiusMm");

            // clamp fractions to [0,1]
            t1 = Math.Max(0.0, Math.Min(1.0, t1));
            t2 = Math.Max(0.0, Math.Min(1.0, t2));

            var level = doc.GetElement(room.LevelId) as Level;
            double levelZFt = level?.Elevation ?? 0.0;
            double levelElevMm = UnitHelper.FtToMm(levelZFt);

            // Room bounding box → 中間高さで IsPointInRoom を評価
            var roomBb = room.get_BoundingBox(null);
            if (roomBb == null)
                return ResultUtil.Err("Room bounding box could not be determined.");

            double zMid = 0.5 * (roomBb.Min.Z + roomBb.Max.Z);

            bool IsInsideRoom(XYZ pt)
            {
                var pz = new XYZ(pt.X, pt.Y, zMid);
                try { return room.IsPointInRoom(pz); }
                catch { return false; }
            }

            // Collect candidate walls on same / spanning level
            var allWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements()
                .OfType<Wall>()
                .ToList();

            var candidates = new List<Wall>();
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

                    // Height: WALL_USER_HEIGHT_PARAM if positive, else bounding box Z span
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
                        candidates.Add(w);
                }
                catch
                {
                    // ignore problematic walls
                }
            }

            // Optional XY search radius around room center to limit candidates
            XYZ? roomCenterFt = null;
            try
            {
                var centerParam = room.Location as LocationPoint;
                if (centerParam != null)
                    roomCenterFt = centerParam.Point;
            }
            catch { }

            double radiusSqMm = searchRadiusMm.HasValue ? searchRadiusMm.Value * searchRadiusMm.Value : double.PositiveInfinity;

            bool WithinRadius(XYZ pFt)
            {
                if (!searchRadiusMm.HasValue || roomCenterFt == null) return true;
                double xMm = UnitHelper.FtToMm(pFt.X);
                double yMm = UnitHelper.FtToMm(pFt.Y);
                double cxMm = UnitHelper.FtToMm(roomCenterFt.X);
                double cyMm = UnitHelper.FtToMm(roomCenterFt.Y);
                double dx = xMm - cxMm;
                double dy = yMm - cyMm;
                return (dx * dx + dy * dy) <= radiusSqMm;
            }

            var results = new List<object>();

            foreach (var wall in candidates)
            {
                var locCurve = wall.Location as LocationCurve;
                var curve = locCurve?.Curve;
                if (curve == null) continue;

                try
                {
                    var p33 = curve.Evaluate(t1, true);
                    var p67 = curve.Evaluate(t2, true);

                    if (!WithinRadius(p33) && !WithinRadius(p67))
                        continue;

                    bool inside33 = IsInsideRoom(p33);
                    bool inside67 = IsInsideRoom(p67);

                    if (!inside33 || !inside67)
                        continue;

                    var wt = wall.WallType;

                    var p33mm = UnitHelper.XyzToMm(p33);
                    var p67mm = UnitHelper.XyzToMm(p67);

                    results.Add(new
                    {
                        wallId = wall.Id.IntegerValue,
                        uniqueId = wall.UniqueId,
                        typeId = wt != null ? wt.Id.IntegerValue : 0,
                        typeName = wt != null ? wt.Name ?? string.Empty : string.Empty,
                        levelId = level != null ? level.Id.IntegerValue : 0,
                        levelName = level?.Name ?? string.Empty,
                        sample = new
                        {
                            t1 = t1,
                            t2 = t2,
                            p1 = new
                            {
                                x = Math.Round(p33mm.x, 3),
                                y = Math.Round(p33mm.y, 3),
                                z = Math.Round(UnitHelper.FtToMm(zMid), 3)
                            },
                            p2 = new
                            {
                                x = Math.Round(p67mm.x, 3),
                                y = Math.Round(p67mm.y, 3),
                                z = Math.Round(UnitHelper.FtToMm(zMid), 3)
                            }
                        }
                    });
                }
                catch
                {
                    // ignore curve evaluation problems
                }
            }

            return ResultUtil.Ok(new
            {
                roomId,
                levelId = level != null ? level.Id.IntegerValue : 0,
                levelName = level?.Name ?? string.Empty,
                t1,
                t2,
                searchRadiusMm,
                wallCount = results.Count,
                walls = results.ToArray()
            });
        }
    }
}
