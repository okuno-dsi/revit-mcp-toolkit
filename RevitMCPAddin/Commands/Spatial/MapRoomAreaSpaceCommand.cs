#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture; // Room, Area (alias via using if needed)
using Autodesk.Revit.DB.Mechanical;   // Space
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    /// <summary>
    /// JSON-RPC: map_room_area_space
    /// Map a Room to nearest Area/Space on the same Level via (1) name/number match, then (2) planar centroid nearest.
    /// Params: { roomId:int, strategy?:"name_then_nearest"|"nearest_only" }
    /// Result: { ok, room:{id,level,name,number}, area:{mode,elementId,name,number,distanceMm?}, space:{mode,elementId,name,number,distanceMm?} }
    /// </summary>
    public class MapRoomAreaSpaceCommand : IRevitCommandHandler
    {
        public string CommandName => "map_room_area_space";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());
            var roomId = p.Value<int?>("roomId");
            if (!roomId.HasValue || roomId.Value <= 0) return ResultUtil.Err("roomId must be specified (>0)");

            var strategy = (p.Value<string>("strategy") ?? "name_then_nearest").Trim().ToLowerInvariant();
            var centroidMode = (p.Value<string>("centroidMode") ?? "auto").Trim().ToLowerInvariant(); // auto|approx|exact
            int maxScan = p.Value<int?>("maxScan") ?? 0;
            int maxMillis = p.Value<int?>("maxMillis") ?? 0;
            bool emitFactorsOnly = p.Value<bool?>("emitFactorsOnly") ?? false;

            var room = doc.GetElement(new ElementId(roomId.Value)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return ResultUtil.Err("Room not found.");

            string roomName = Safe(room.Name);
            string roomNumber = Safe(room.Number);
            var roomLevelId = room.LevelId;
            string roomLevelName = (doc.GetElement(roomLevelId) as Level)?.Name ?? string.Empty;

            // Collect Areas and Spaces on the same level.
            // Revit 2024 以降では、Area / Space を直接 OfClass(typeof(Area/Space)) で
            // 取得しようとすると
            //   "Input type(Autodesk.Revit.DB.Mechanical.Space) is of an element type ..."
            // という例外が出るため、SpatialElement + Category 絞り込みで取得する。
            var areasSameLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                            e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Areas)
                .Cast<SpatialElement>()
                .OfType<Autodesk.Revit.DB.Area>()
                .Where(a => a != null && a.LevelId == roomLevelId)
                .ToList();

            var spacesSameLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                            e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_MEPSpaces)
                .Cast<SpatialElement>()
                .OfType<Autodesk.Revit.DB.Mechanical.Space>()
                .Where(s => s != null && s.LevelId == roomLevelId)
                .ToList();

            // 1) Name/number match first
            Autodesk.Revit.DB.Area? areaMatch = null;
            Autodesk.Revit.DB.Mechanical.Space? spaceMatch = null;
            if (strategy == "name_then_nearest")
            {
                if (!string.IsNullOrWhiteSpace(roomNumber))
                {
                    areaMatch = areasSameLevel.FirstOrDefault(a => Safe(a.Number) == roomNumber || Safe(a.Name) == roomNumber);
                    spaceMatch = spacesSameLevel.FirstOrDefault(s => Safe(s.Number) == roomNumber || Safe(s.Name) == roomNumber);
                }
                if (areaMatch == null && !string.IsNullOrWhiteSpace(roomName))
                    areaMatch = areasSameLevel.FirstOrDefault(a => Safe(a.Name) == roomName || Safe(a.Number) == roomName);
                if (spaceMatch == null && !string.IsNullOrWhiteSpace(roomName))
                    spaceMatch = spacesSameLevel.FirstOrDefault(s => Safe(s.Name) == roomName || Safe(s.Number) == roomName);
            }

            // 2) Nearest centroid fallback
            (double xMm, double yMm) roomC2d = GetRoomPlanarCentroidMm(doc, room);

            (Autodesk.Revit.DB.Area? best, double dist) AreaNearest()
            {
                Autodesk.Revit.DB.Area? bestA = null; double bestD = double.MaxValue;
                foreach (var a in areasSameLevel)
                {
                    var c = GetAreaCentroidAuto(doc, a, centroidMode);
                    var d = DistMm(roomC2d, c);
                    if (d < bestD) { bestD = d; bestA = a; }
                }
                return (bestA, bestD);
            }

            (Autodesk.Revit.DB.Mechanical.Space? best, double dist) SpaceNearest()
            {
                Autodesk.Revit.DB.Mechanical.Space? bestS = null; double bestD = double.MaxValue;
                foreach (var s in spacesSameLevel)
                {
                    var c = GetSpaceCentroidAuto(doc, s, centroidMode);
                    var d = DistMm(roomC2d, c);
                    if (d < bestD) { bestD = d; bestS = s; }
                }
                return (bestS, bestD);
            }

            string areaMode = null; double areaDist = 0;
            if (areaMatch != null) { areaMode = "name_match"; areaDist = 0; }
            else
            {
                var nn = AreaNearest();
                areaMatch = nn.best; areaMode = areaMatch != null ? "nearest_centroid" : null; areaDist = nn.dist;
            }

            string spaceMode = null; double spaceDist = 0;
            if (spaceMatch != null) { spaceMode = "name_match"; spaceDist = 0; }
            else
            {
                var nn = SpaceNearest();
                spaceMatch = nn.best; spaceMode = spaceMatch != null ? "nearest_centroid" : null; spaceDist = nn.dist;
            }

            var result = new
            {
                ok = true,
                room = new { id = room.Id.IntegerValue, level = roomLevelName, name = roomName, number = roomNumber },
                area = areaMatch == null ? null : new
                {
                    mode = areaMode,
                    elementId = areaMatch.Id.IntegerValue,
                    name = Safe(areaMatch.Name),
                    number = Safe(areaMatch.Number),
                    distanceMm = areaMode == "nearest_centroid" ? Math.Round(areaDist, 3) : (double?)null
                },
                space = spaceMatch == null ? null : new
                {
                    mode = spaceMode,
                    elementId = spaceMatch.Id.IntegerValue,
                    name = Safe(spaceMatch.Name),
                    number = Safe(spaceMatch.Number),
                    distanceMm = spaceMode == "nearest_centroid" ? Math.Round(spaceDist, 3) : (double?)null
                },
                factors = emitFactorsOnly ? new
                {
                    room = new { level = roomLevelName, centroid = new { xMm = Math.Round(roomC2d.xMm, 3), yMm = Math.Round(roomC2d.yMm, 3) } },
                    area = areaMatch == null ? null : new { level = roomLevelName, centroid = ToFactorsCentroid(centroidMode == "exact" ? GetAreaCentroidAuto(doc, areaMatch, centroidMode) : (areaMatch.get_BoundingBox(null) is BoundingBoxXYZ abb ? (UnitHelper.FtToMm(((abb.Min+abb.Max)*0.5).X), UnitHelper.FtToMm(((abb.Min+abb.Max)*0.5).Y)) : GetAreaCentroidAuto(doc, areaMatch, centroidMode))) },
                    space = spaceMatch == null ? null : new { level = roomLevelName, centroid = ToFactorsCentroid(centroidMode == "exact" ? GetSpaceCentroidAuto(doc, spaceMatch, centroidMode) : (spaceMatch.get_BoundingBox(null) is BoundingBoxXYZ sbb ? (UnitHelper.FtToMm(((sbb.Min+sbb.Max)*0.5).X), UnitHelper.FtToMm(((sbb.Min+sbb.Max)*0.5).Y)) : GetSpaceCentroidAuto(doc, spaceMatch, centroidMode))) }
                } : null
            };

            return ResultUtil.Ok(result);
        }

        private static string Safe(string? s) => s ?? string.Empty;

        private static double DistMm((double xMm, double yMm) a, (double xMm, double yMm) b)
        {
            var dx = a.xMm - b.xMm; var dy = a.yMm - b.yMm; return Math.Sqrt(dx * dx + dy * dy);
        }

        private static object ToFactorsCentroid((double xMm, double yMm) c)
            => new { xMm = Math.Round(c.xMm, 3), yMm = Math.Round(c.yMm, 3) };

        private static (double xMm, double yMm) GetRoomPlanarCentroidMm(Document doc, Autodesk.Revit.DB.Architecture.Room room)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = room.GetBoundarySegments(opts);
                if (loops == null || loops.Count == 0) return FromPoint(room.Location as LocationPoint);

                // choose loop with longest perimeter
                List<XYZ> outer = null; double bestLen = -1;
                foreach (var segs in loops)
                {
                    var pts = new List<XYZ>(); double sum = 0;
                    foreach (var bs in segs)
                    {
                        var c = bs.GetCurve(); sum += c.Length;
                        var p0 = c.GetEndPoint(0); if (pts.Count == 0 || !pts.Last().IsAlmostEqualTo(p0)) pts.Add(p0);
                        var p1 = c.GetEndPoint(1); if (!pts.Last().IsAlmostEqualTo(p1)) pts.Add(p1);
                    }
                    if (sum > bestLen) { bestLen = sum; outer = pts; }
                }
                if (outer == null || outer.Count < 3) return FromPoint(room.Location as LocationPoint);
                var (cx, cy, _) = Centroid2D(outer);
                return (UnitHelper.FtToMm(cx), UnitHelper.FtToMm(cy));
            }
            catch { return FromPoint(room.Location as LocationPoint); }
        }

        private static (double xMm, double yMm) FromPoint(LocationPoint? lp)
        { if (lp?.Point == null) return (0, 0); return (UnitHelper.FtToMm(lp.Point.X), UnitHelper.FtToMm(lp.Point.Y)); }

        private static (double xMm, double yMm) GetAreaCentroidMm(Document doc, Autodesk.Revit.DB.Area area)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = area.GetBoundarySegments(opts);
                if (loops == null || loops.Count == 0) return (0, 0);
                double sumA = 0, sumCx = 0, sumCy = 0;
                foreach (var loop in loops)
                {
                    var pts = new List<XYZ>();
                    foreach (var s in loop)
                    {
                        var c = s.GetCurve();
                        var tess = (c is Line) ? new List<XYZ> { c.GetEndPoint(0), c.GetEndPoint(1) } : c.Tessellate().ToList();
                        pts.AddRange(tess);
                    }
                    if (pts.Count < 3) continue;
                    if (!pts[0].IsAlmostEqualTo(pts[pts.Count - 1])) pts.Add(pts[0]);
                    double A = 0, Cx = 0, Cy = 0;
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        var p0 = pts[i]; var p1 = pts[i + 1];
                        double x0 = UnitHelper.FtToMm(p0.X), y0 = UnitHelper.FtToMm(p0.Y);
                        double x1 = UnitHelper.FtToMm(p1.X), y1 = UnitHelper.FtToMm(p1.Y);
                        double cross = x0 * y1 - x1 * y0; A += cross; Cx += (x0 + x1) * cross; Cy += (y0 + y1) * cross;
                    }
                    A *= 0.5; if (Math.Abs(A) < 1e-9) continue; Cx /= (6 * A); Cy /= (6 * A);
                    sumA += A; sumCx += Cx * A; sumCy += Cy * A;
                }
                if (Math.Abs(sumA) < 1e-9) return (0, 0);
                return (sumCx / sumA, sumCy / sumA);
            }
            catch { return (0, 0); }
        }

        private static (double xMm, double yMm) GetSpaceCentroidMm(Document doc, Autodesk.Revit.DB.Mechanical.Space s)
        {
            try
            {
                if (s.Location is LocationPoint lp && lp.Point != null)
                    return (UnitHelper.FtToMm(lp.Point.X), UnitHelper.FtToMm(lp.Point.Y));
                var bb = s.get_BoundingBox(null);
                if (bb != null)
                {
                    var c = (bb.Min + bb.Max) * 0.5; return (UnitHelper.FtToMm(c.X), UnitHelper.FtToMm(c.Y));
                }
            }
            catch { }
            return (0, 0);
        }

        // Auto centroid helpers (approximate via bounding box unless exact requested)
        private static (double xMm, double yMm) GetAreaCentroidAuto(Document doc, Autodesk.Revit.DB.Area area, string centroidMode)
        {
            if (string.Equals(centroidMode, "exact", StringComparison.OrdinalIgnoreCase))
                return GetAreaCentroidMm(doc, area);
            try
            {
                var bb = area.get_BoundingBox(null);
                if (bb != null)
                {
                    var c = (bb.Min + bb.Max) * 0.5;
                    return (UnitHelper.FtToMm(c.X), UnitHelper.FtToMm(c.Y));
                }
            }
            catch { }
            return GetAreaCentroidMm(doc, area);
        }

        private static (double xMm, double yMm) GetSpaceCentroidAuto(Document doc, Autodesk.Revit.DB.Mechanical.Space s, string centroidMode)
        {
            if (string.Equals(centroidMode, "exact", StringComparison.OrdinalIgnoreCase))
                return GetSpaceCentroidMm(doc, s);
            try
            {
                if (s.Location is LocationPoint lp && lp.Point != null)
                    return (UnitHelper.FtToMm(lp.Point.X), UnitHelper.FtToMm(lp.Point.Y));
                var bb = s.get_BoundingBox(null);
                if (bb != null)
                {
                    var c = (bb.Min + bb.Max) * 0.5;
                    return (UnitHelper.FtToMm(c.X), UnitHelper.FtToMm(c.Y));
                }
            }
            catch { }
            return GetSpaceCentroidMm(doc, s);
        }

        private static (double cx, double cy, double area2) Centroid2D(IList<XYZ> pts)
        {
            int n = pts.Count; if (n < 3) return (0, 0, 0);
            double A = 0, Cx = 0, Cy = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                double cross = p.X * q.Y - q.X * p.Y; A += cross; Cx += (p.X + q.X) * cross; Cy += (p.Y + q.Y) * cross;
            }
            if (Math.Abs(A) < 1e-12) return (0, 0, 0);
            A *= 0.5; Cx /= (6 * A); Cy /= (6 * A); return (Cx, Cy, A * 2);
        }
    }
}
 
