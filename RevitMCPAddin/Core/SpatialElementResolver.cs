// ================================================================
// File: Core/SpatialElementResolver.cs
// Purpose: Spatial selection correction helper (Room / Space / Area)
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes  :
//  - Designed to fix common mismatch: user selected Space/Area but a Room command expects Room (and vice versa).
//  - Does not modify the model; only resolves which spatial element to operate on.
//  - Logging uses RevitLogger policy (%LOCALAPPDATA%\\RevitMCP\\logs).
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;

namespace RevitMCPAddin.Core
{
    internal enum SpatialKind { Room, Space, Area }

    internal sealed class SpatialResolveOptions
    {
        // Revit internal units (feet)
        public double MaxDistanceInternal { get; set; }
        public bool PreferContainment { get; set; } = true;
        public bool PreferSameLevel { get; set; } = true;
        public bool AllowTags { get; set; } = true;

        public static SpatialResolveOptions CreateDefaultMeters(double maxDistanceMeters = 0.5)
        {
            var m = maxDistanceMeters;
            if (double.IsNaN(m) || double.IsInfinity(m) || m <= 0) m = 0.5;
            return new SpatialResolveOptions
            {
                MaxDistanceInternal = UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters)
            };
        }
    }

    internal sealed class SpatialResolveResult
    {
        public bool Ok { get; set; }
        public ElementId OriginalId { get; set; } = ElementId.InvalidElementId;
        public SpatialKind? OriginalKind { get; set; }
        public ElementId ResolvedId { get; set; } = ElementId.InvalidElementId;
        public SpatialKind ResolvedKind { get; set; }
        public double DistanceInternal { get; set; }  // feet
        public bool ByContainment { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    internal static class SpatialElementResolver
    {
        public static bool TryResolve(
            Document doc,
            ElementId inputId,
            SpatialKind desiredKind,
            SpatialResolveOptions opt,
            out SpatialResolveResult result)
        {
            result = new SpatialResolveResult
            {
                Ok = false,
                OriginalId = inputId ?? ElementId.InvalidElementId,
                ResolvedId = ElementId.InvalidElementId,
                ResolvedKind = desiredKind,
                DistanceInternal = double.NaN,
                ByContainment = false,
                Message = string.Empty
            };

            if (doc == null)
            {
                result.Message = "Document is null.";
                return false;
            }

            if (inputId == null || inputId == ElementId.InvalidElementId)
            {
                result.Message = "Invalid selection (elementId).";
                return false;
            }

            Element original = null;
            try { original = doc.GetElement(inputId); } catch { original = null; }
            if (original == null)
            {
                result.Message = "Selected element not found (may have been deleted).";
                return false;
            }

            if (opt != null && opt.AllowTags)
            {
                Element unwrapped = TryUnwrapSpatialTag(original);
                if (unwrapped != null) original = unwrapped;
            }

            result.OriginalId = original.Id;
            result.OriginalKind = DetectKind(original);

            if (IsDesiredKind(original, desiredKind))
            {
                result.Ok = true;
                result.ResolvedId = original.Id;
                result.DistanceInternal = 0.0;
                result.ByContainment = true;
                result.Message = "Selection already matches the desired spatial kind.";
                return true;
            }

            XYZ refPt;
            if (!TryGetReferencePoint(original, out refPt))
            {
                result.Message = "Cannot resolve selection because reference point could not be computed (no Location/BBox).";
                return false;
            }

            ElementId levelId = TryGetLevelId(original);

            double maxDist = (opt != null)
                ? opt.MaxDistanceInternal
                : SpatialResolveOptions.CreateDefaultMeters().MaxDistanceInternal;
            if (maxDist <= 0 || double.IsNaN(maxDist) || double.IsInfinity(maxDist))
                maxDist = SpatialResolveOptions.CreateDefaultMeters().MaxDistanceInternal;

            // Collect candidate spatial elements near the reference point.
            BuiltInCategory bic = ToBuiltInCategory(desiredKind);

            FilteredElementCollector col = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType();

            // Category filter (best-effort)
            try
            {
                col = col.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { bic }));
            }
            catch
            {
                // fallback: later type checks will filter
            }

            // Near filter
            try
            {
                var near = new Outline(
                    new XYZ(refPt.X - maxDist, refPt.Y - maxDist, refPt.Z - maxDist),
                    new XYZ(refPt.X + maxDist, refPt.Y + maxDist, refPt.Z + maxDist));
                col = col.WherePasses(new BoundingBoxIntersectsFilter(near));
            }
            catch
            {
                // ignore
            }

            // Same-level preference
            if (opt != null && opt.PreferSameLevel && levelId != ElementId.InvalidElementId)
            {
                try { col = col.WherePasses(new ElementLevelFilter(levelId)); } catch { /* ignore */ }
            }

            Element best = null;
            double bestDist = double.MaxValue;
            bool bestByContainment = false;

            foreach (Element cand in col)
            {
                if (cand == null) continue;
                if (!IsDesiredKind(cand, desiredKind)) continue;

                XYZ candPt;
                if (!TryGetReferencePoint(cand, out candPt)) continue;

                double dist = candPt.DistanceTo(refPt);
                bool contains = false;

                if (opt != null && opt.PreferContainment)
                {
                    var se = cand as SpatialElement;
                    if (se != null)
                        contains = SpatialElementContainsPoint2D(se, refPt);
                }

                if (contains)
                {
                    if (!bestByContainment || dist < bestDist)
                    {
                        best = cand;
                        bestDist = dist;
                        bestByContainment = true;
                    }
                }
                else
                {
                    if (!bestByContainment && dist < bestDist)
                    {
                        best = cand;
                        bestDist = dist;
                        bestByContainment = false;
                    }
                }
            }

            if (best == null)
            {
                result.Message = "No nearby " + desiredKind + " found. (Not placed/not enclosed/other level/too far.)";
                return false;
            }

            if (!bestByContainment && bestDist > maxDist)
            {
                double dM = UnitUtils.ConvertFromInternalUnits(bestDist, UnitTypeId.Meters);
                double maxM = UnitUtils.ConvertFromInternalUnits(maxDist, UnitTypeId.Meters);
                result.Message = desiredKind + " found, but distance exceeds threshold (" + dM.ToString("0.###") + "m > " + maxM.ToString("0.###") + "m).";
                return false;
            }

            result.Ok = true;
            result.ResolvedId = best.Id;
            result.DistanceInternal = bestDist;
            result.ByContainment = bestByContainment;

            string distMsg = UnitUtils.ConvertFromInternalUnits(bestDist, UnitTypeId.Meters).ToString("0.###") + "m";
            result.Message = bestByContainment
                ? "Resolved to " + desiredKind + " by containment (distance " + distMsg + ")."
                : "Resolved to " + desiredKind + " by nearest match (distance " + distMsg + ").";

            return true;
        }

        private static Element TryUnwrapSpatialTag(Element e)
        {
            if (e == null) return null;

            // Unwrap tags to underlying spatial element if possible.
            if (e is RoomTag rt)
            {
                try { if (rt.Room != null) return rt.Room; } catch { }
            }
            if (e is SpaceTag st)
            {
                try { if (st.Space != null) return st.Space; } catch { }
            }
            // AreaTag lives in Autodesk.Revit.DB (not Architecture)
            if (e is AreaTag at)
            {
                try { if (at.Area != null) return at.Area; } catch { }
            }
            return e;
        }

        private static SpatialKind? DetectKind(Element e)
        {
            if (e is Room) return SpatialKind.Room;
            if (e is Space) return SpatialKind.Space;
            if (e is Area) return SpatialKind.Area;
            return null;
        }

        private static bool IsDesiredKind(Element e, SpatialKind desired)
        {
            switch (desired)
            {
                case SpatialKind.Room: return e is Room;
                case SpatialKind.Space: return e is Space;
                case SpatialKind.Area: return e is Area;
                default: return false;
            }
        }

        private static BuiltInCategory ToBuiltInCategory(SpatialKind kind)
        {
            switch (kind)
            {
                case SpatialKind.Room: return BuiltInCategory.OST_Rooms;
                case SpatialKind.Space: return BuiltInCategory.OST_MEPSpaces;
                case SpatialKind.Area: return BuiltInCategory.OST_Areas;
                default: return BuiltInCategory.INVALID;
            }
        }

        private static bool TryGetReferencePoint(Element e, out XYZ p)
        {
            p = null;
            if (e == null) return false;

            try
            {
                // Prefer shared helper (LocationPoint/LocationCurve midpoint/BBox center).
                string msg;
                var rp = SpatialUtils.GetReferencePoint(e.Document, e, out msg);
                if (rp != null)
                {
                    p = rp;
                    return true;
                }
            }
            catch
            {
                // ignore and fallback to local implementation
            }

            try
            {
                if (e.Location is LocationPoint lp && lp.Point != null)
                {
                    p = lp.Point;
                    return true;
                }

                if (e.Location is LocationCurve lc && lc.Curve != null)
                {
                    XYZ a = lc.Curve.GetEndPoint(0);
                    XYZ b = lc.Curve.GetEndPoint(1);
                    p = new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
                    return true;
                }

                var bb = e.get_BoundingBox(null);
                if (bb != null)
                {
                    p = new XYZ(
                        (bb.Min.X + bb.Max.X) * 0.5,
                        (bb.Min.Y + bb.Max.Y) * 0.5,
                        (bb.Min.Z + bb.Max.Z) * 0.5
                    );
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static ElementId TryGetLevelId(Element e)
        {
            if (e == null) return ElementId.InvalidElementId;

            try
            {
                if (e is SpatialElement se)
                    return se.LevelId;
            }
            catch { /* ignore */ }

            try
            {
                var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != null) return id;
                }
            }
            catch { /* ignore */ }

            return ElementId.InvalidElementId;
        }

        private static bool SpatialElementContainsPoint2D(SpatialElement se, XYZ refPt)
        {
            if (se == null) return false;

            IList<IList<BoundarySegment>> loops = null;
            try
            {
                var opt = new SpatialElementBoundaryOptions();
                loops = se.GetBoundarySegments(opt);
            }
            catch
            {
                return false;
            }

            if (loops == null) return false;

            foreach (IList<BoundarySegment> loop in loops)
            {
                if (loop == null || loop.Count < 3) continue;

                var poly = new List<XYZ>(loop.Count);
                foreach (BoundarySegment seg in loop)
                {
                    Curve c = null;
                    try { c = seg.GetCurve(); } catch { c = null; }
                    if (c == null) continue;
                    try { poly.Add(c.GetEndPoint(0)); } catch { }
                }

                if (poly.Count >= 3 && PointInPolygonXY(refPt, poly))
                    return true;
            }

            return false;
        }

        private static bool PointInPolygonXY(XYZ p, List<XYZ> poly)
        {
            double x = p.X;
            double y = p.Y;
            bool inside = false;

            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;

                bool intersect = ((yi > y) != (yj > y))
                    && (x < (xj - xi) * (y - yi) / ((yj - yi) == 0.0 ? 1e-9 : (yj - yi)) + xi);

                if (intersect) inside = !inside;
            }

            return inside;
        }
    }
}

