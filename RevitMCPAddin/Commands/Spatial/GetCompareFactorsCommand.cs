#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    /// <summary>
    /// JSON-RPC: get_compare_factors
    /// Return lightweight comparison factors for elements: {elementId, categoryId/name, levelId/name, centroid(xMm,yMm,zMm), bbox(dxMm,dyMm,dzMm)}.
    /// Params: { elementIds:int[] }
    /// </summary>
    public class GetCompareFactorsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_compare_factors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());
            var ids = p["elementIds"] as JArray;
            if (ids == null || ids.Count == 0) return ResultUtil.Err("elementIds must be provided.");

            var list = new List<object>();
            foreach (var t in ids)
            {
                try
                {
                    var id = Autodesk.Revit.DB.ElementIdCompat.From(t.Value<int>());
                    var e = doc.GetElement(id);
                    if (e == null) continue;
                    var cat = e.Category;
                    var levelId = GetLevelId(e);
                    var levelName = levelId != null ? (doc.GetElement(levelId) as Level)?.Name : null;
                    var c = GetCentroidMm(e);
                    var bb = e.get_BoundingBox(null);
                    double dx=0, dy=0, dz=0;
                    if (bb != null)
                    {
                        dx = UnitHelper.FtToMm(Math.Abs(bb.Max.X - bb.Min.X));
                        dy = UnitHelper.FtToMm(Math.Abs(bb.Max.Y - bb.Min.Y));
                        dz = UnitHelper.FtToMm(Math.Abs(bb.Max.Z - bb.Min.Z));
                    }
                    list.Add(new
                    {
                        elementId = e.Id.IntValue(),
                        categoryId = cat?.Id?.IntValue(),
                        category = cat?.Name,
                        levelId = levelId?.IntValue(),
                        level = levelName,
                        centroid = new { xMm = Math.Round(c.xMm,3), yMm = Math.Round(c.yMm,3), zMm = Math.Round(c.zMm,3) },
                        bbox = new { dxMm = Math.Round(dx,3), dyMm = Math.Round(dy,3), dzMm = Math.Round(dz,3) }
                    });
                }
                catch { }
            }
            return ResultUtil.Ok(new { ok = true, items = list });
        }

        private static ElementId? GetLevelId(Element e)
        {
            try
            {
                var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM) ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId) return p.AsElementId();
            }
            catch { }
            return null;
        }

        private static (double xMm, double yMm, double zMm) GetCentroidMm(Element e)
        {
            try
            {
                switch (e)
                {
                    case Autodesk.Revit.DB.Architecture.Room r:
                        var rc = RoomCentroid(r); return (UnitHelper.FtToMm(rc.X), UnitHelper.FtToMm(rc.Y), UnitHelper.FtToMm(rc.Z));
                    case Autodesk.Revit.DB.Area a:
                        var ac = AreaCentroid(a); return (UnitHelper.FtToMm(ac.X), UnitHelper.FtToMm(ac.Y), 0);
                    case Autodesk.Revit.DB.Mechanical.Space s:
                        if (s.Location is LocationPoint lp && lp.Point != null) return (UnitHelper.FtToMm(lp.Point.X), UnitHelper.FtToMm(lp.Point.Y), UnitHelper.FtToMm(lp.Point.Z));
                        break;
                }
                if (e.Location is LocationPoint lp2 && lp2.Point != null)
                    return (UnitHelper.FtToMm(lp2.Point.X), UnitHelper.FtToMm(lp2.Point.Y), UnitHelper.FtToMm(lp2.Point.Z));
                var bb = e.get_BoundingBox(null);
                if (bb != null)
                {
                    var c = (bb.Min + bb.Max) * 0.5; return (UnitHelper.FtToMm(c.X), UnitHelper.FtToMm(c.Y), UnitHelper.FtToMm(c.Z));
                }
            }
            catch { }
            return (0, 0, 0);
        }

        private static XYZ RoomCentroid(Autodesk.Revit.DB.Architecture.Room room)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = room.GetBoundarySegments(opts);
                if (loops != null && loops.Count > 0)
                {
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
                    if (outer != null && outer.Count >= 3)
                    {
                        var (cx, cy, _) = Centroid2D(outer); double z = 0;
                        if (room.Location is LocationPoint lp && lp.Point != null) z = lp.Point.Z;
                        return new XYZ(cx, cy, z);
                    }
                }
                if (room.Location is LocationPoint lp2 && lp2.Point != null) return lp2.Point;
            }
            catch { }
            return new XYZ(0, 0, 0);
        }

        private static XYZ AreaCentroid(Autodesk.Revit.DB.Area area)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions();
                var loops = area.GetBoundarySegments(opts);
                if (loops == null || loops.Count == 0) return new XYZ(0, 0, 0);
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
                        double x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;
                        double cross = x0 * y1 - x1 * y0; A += cross; Cx += (x0 + x1) * cross; Cy += (y0 + y1) * cross;
                    }
                    A *= 0.5; if (Math.Abs(A) < 1e-9) continue; Cx /= (6 * A); Cy /= (6 * A);
                    sumA += A; sumCx += Cx * A; sumCy += Cy * A;
                }
                if (Math.Abs(sumA) < 1e-9) return new XYZ(0, 0, 0);
                return new XYZ(sumCx / sumA, sumCy / sumA, 0);
            }
            catch { return new XYZ(0, 0, 0); }
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






