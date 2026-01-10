#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCPAddin.Models;

namespace RevitMCPAddin.Core.Walls
{
    public static class WallFlushPlacement
    {
        private enum WallSide { Exterior, Interior }
        private enum SideMode { ByGlobalDirection, ByExterior, ByInterior }
        private enum PlaneRef { FinishFace, CoreFace, WallCenterline, CoreCenterline }
        private enum NewExteriorMode { AwayFromSource, MatchSourceExterior, OppositeSourceExterior }

        private sealed class WallSectionData
        {
            public double TotalWidth;
            public double ExteriorShell;
            public double InteriorShell;
            public double CoreWidth;
        }

        private sealed class Segment
        {
            public Autodesk.Revit.DB.Wall SourceWall = null!;
            public Curve SourceCurve = null!;
            public Curve NewCurve = null!;
            public WallType SourceWallType = null!;

            public PlaneRef SourcePlane;
            public PlaneRef NewPlane;

            public XYZ SourceExteriorDir = XYZ.Zero;
            public WallSide SourceContactSide;
            public XYZ PlacementDir = XYZ.Zero; // source -> new wall

            public XYZ NewExteriorDir = XYZ.Zero;
            public bool NewFlip;

            public double OffsetAlongPlacement;
        }

        public static CreateFlushWallsResponse Execute(Document doc, CreateFlushWallsRequest req)
        {
            var res = new CreateFlushWallsResponse();
            if (doc == null) { res.Ok = false; res.Message = "Document is null."; return res; }
            if (req == null) { res.Ok = false; res.Message = "Request is null."; return res; }

            var sourceIds = req.SourceWallIds ?? new List<int>();
            if (sourceIds.Count == 0)
            {
                res.Ok = false;
                res.Message = "SourceWallIds is empty.";
                return res;
            }

            var typeKey = (req.NewWallTypeNameOrId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(typeKey))
            {
                res.Ok = false;
                res.Message = "NewWallTypeNameOrId is required.";
                return res;
            }

            var newWallType = FindWallType(doc, typeKey);
            if (newWallType == null)
            {
                res.Ok = false;
                res.Message = "WallType not found: " + typeKey;
                return res;
            }

            var sideMode = ParseEnum(req.SideMode, SideMode.ByGlobalDirection);
            var sourcePlane = ParseEnum(req.SourcePlane, PlaneRef.FinishFace);
            var newPlane = string.IsNullOrWhiteSpace(req.NewPlane) ? sourcePlane : ParseEnum(req.NewPlane, sourcePlane);
            var newExteriorMode = ParseEnum(req.NewExteriorMode, NewExteriorMode.MatchSourceExterior);

            var globalDir = Normalize2D(ToXyz(req.GlobalDirection, fallback: new XYZ(0, -1, 0)));
            if (globalDir.IsZeroLength()) globalDir = new XYZ(0, -1, 0);

            // Collect walls (best-effort; keep input order)
            var sourceWalls = new List<Autodesk.Revit.DB.Wall>();
            foreach (var idInt in sourceIds)
            {
                if (idInt <= 0) continue;
                var w = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)) as Autodesk.Revit.DB.Wall;
                if (w == null) { res.Warnings.Add("Not a wall: " + idInt); continue; }
                sourceWalls.Add(w);
            }

            if (sourceWalls.Count == 0)
            {
                res.Ok = false;
                res.Message = "No valid source walls.";
                return res;
            }

            // Best-effort chain ordering (improves miter when multiple walls are selected)
            sourceWalls = BuildOrderedChain(sourceWalls, res.Warnings);

            // Calibrate curve->exterior convention once (heuristic)
            bool useZCrossV = CalibrateZCrossVConvention(sourceWalls[0], res.Warnings);

            // Build segments (compute offset curves)
            var segments = new List<Segment>();
            foreach (var src in sourceWalls)
            {
                if (!TryGetWallCurve(src, out var srcCurve))
                {
                    res.Warnings.Add("Wall " + src.Id.IntValue() + ": no LocationCurve.");
                    continue;
                }

                var srcType = doc.GetElement(src.GetTypeId()) as WallType;
                if (srcType == null)
                {
                    res.Warnings.Add("Wall " + src.Id.IntValue() + ": WallType not found.");
                    continue;
                }

                var seg = new Segment();
                seg.SourceWall = src;
                seg.SourceCurve = srcCurve;
                seg.SourceWallType = srcType;
                seg.SourcePlane = sourcePlane;
                seg.NewPlane = newPlane;

                // Source exterior dir
                if (!TryGetFinishFaceNormal2D(src, WallSide.Exterior, out var srcExterior))
                {
                    srcExterior = Normalize2D(src.Orientation);
                    if (srcExterior.IsZeroLength()) srcExterior = new XYZ(1, 0, 0);
                }
                seg.SourceExteriorDir = srcExterior;

                seg.SourceContactSide = DetermineSourceContactSide(src, sideMode, globalDir, srcExterior, res.Warnings);
                seg.PlacementDir = GetPlacementDir(src, seg.SourceContactSide, srcExterior, res.Warnings);

                // New wall exterior dir and flip (from curve direction)
                seg.NewExteriorDir = DetermineNewExteriorDir(newExteriorMode, seg.PlacementDir, seg.SourceExteriorDir);
                seg.NewFlip = DetermineFlipForDesiredExterior(seg.SourceCurve, seg.NewExteriorDir, useZCrossV);

                // Section data
                var srcData = GetWallSectionData(seg.SourceWallType, seg.SourceWall.Flipped, res.Warnings);
                var newData = GetWallSectionData(newWallType, seg.NewFlip, res.Warnings);

                // Source: centerline -> sourcePlane (signed along source exterior)
                var s_ex = GetSignedDistanceFromCenterline(srcData, seg.SourcePlane, seg.SourceContactSide);
                int signSource = (seg.PlacementDir.DotProduct(seg.SourceExteriorDir) >= 0) ? +1 : -1;
                var s = s_ex * signSource;

                // New: determine which side of new wall is the contact side (facing the source)
                int signNew = (seg.PlacementDir.DotProduct(seg.NewExteriorDir) >= 0) ? +1 : -1;
                WallSide newContactSide = (signNew > 0) ? WallSide.Interior : WallSide.Exterior;

                var n_ex = GetSignedDistanceFromCenterline(newData, seg.NewPlane, newContactSide);
                var n = n_ex * signNew;

                seg.OffsetAlongPlacement = s - n;
                seg.NewCurve = MakeOffsetCurve(seg.SourceCurve, seg.PlacementDir, seg.OffsetAlongPlacement, res.Warnings);
                segments.Add(seg);
            }

            if (segments.Count == 0)
            {
                res.Ok = false;
                res.Message = "No segments were built (see warnings).";
                return res;
            }

            // Miter only for connected line chains (best-effort)
            if (req.MiterJoints && segments.Count >= 2)
            {
                try { ApplyMiterForLineChain(segments, res.Warnings); } catch { /* best-effort */ }
            }

            using (var tx = new Transaction(doc, "Create Flush Walls"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                foreach (var seg in segments)
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try
                        {
                            var created = CreateOneWall(doc, seg, newWallType, req.CopyVerticalConstraints, res.Warnings, useZCrossV);
                            if (created != null) res.CreatedWallIds.Add(created.Id.IntValue());
                            st.Commit();
                        }
                        catch (Exception ex)
                        {
                            res.Warnings.Add("Create failed (wallId=" + seg.SourceWall.Id.IntValue() + "): " + ex.GetType().Name + ": " + ex.Message);
                            try { st.RollBack(); } catch { }
                        }
                    }
                }

                var txStatus = tx.Commit();
                if (txStatus != TransactionStatus.Committed)
                {
                    res.Ok = false;
                    res.Message = "Transaction did not commit: " + txStatus;
                    return res;
                }
            }

            res.Ok = res.CreatedWallIds.Count > 0;
            res.Message = res.Ok ? ("Created " + res.CreatedWallIds.Count + " wall(s).") : "No walls were created (see warnings).";
            return res;
        }

        // ----------------------------
        // Chain ordering (best-effort)
        // ----------------------------

        private static List<Autodesk.Revit.DB.Wall> BuildOrderedChain(List<Autodesk.Revit.DB.Wall> walls, List<string> warnings)
        {
            if (walls == null || walls.Count <= 1) return walls ?? new List<Autodesk.Revit.DB.Wall>();

            // Build a simple "endpoint connected chain" (assumes a single polyline-like chain).
            // If it fails, return input order.
            var infos = new List<Tuple<Autodesk.Revit.DB.Wall, XYZ, XYZ>>();
            foreach (var w in walls)
            {
                if (!TryGetWallCurve(w, out var c))
                {
                    warnings.Add("Wall " + w.Id.IntValue() + ": no curve; excluded from chain ordering.");
                    continue;
                }
                infos.Add(Tuple.Create(w, c.GetEndPoint(0), c.GetEndPoint(1)));
            }

            if (infos.Count <= 1) return infos.Select(x => x.Item1).ToList();

            const double tol = 1e-6; // feet
            var map = new Dictionary<PointKey, List<int>>();

            for (int i = 0; i < infos.Count; i++)
            {
                var k0 = new PointKey(infos[i].Item2, tol);
                var k1 = new PointKey(infos[i].Item3, tol);
                if (!map.ContainsKey(k0)) map[k0] = new List<int>();
                if (!map.ContainsKey(k1)) map[k1] = new List<int>();
                map[k0].Add(i);
                map[k1].Add(i);
            }

            // Find a chain endpoint (degree=1) if possible.
            PointKey? startKey = null;
            foreach (var kv in map)
            {
                if (kv.Value.Count == 1) { startKey = kv.Key; break; }
            }

            int startIndex = 0;
            if (startKey.HasValue) startIndex = map[startKey.Value][0];

            var used = new HashSet<int>();
            var ordered = new List<Autodesk.Revit.DB.Wall>();

            int current = startIndex;
            used.Add(current);
            ordered.Add(infos[current].Item1);

            // Choose current end key: pick endpoint that is NOT startKey (if any)
            PointKey curEndKey;
            {
                var a = new PointKey(infos[current].Item2, tol);
                var b = new PointKey(infos[current].Item3, tol);
                if (startKey.HasValue) curEndKey = a.Equals(startKey.Value) ? b : a;
                else curEndKey = b;
            }

            while (true)
            {
                if (!map.ContainsKey(curEndKey)) break;

                int next = -1;
                foreach (var idx in map[curEndKey])
                {
                    if (!used.Contains(idx)) { next = idx; break; }
                }
                if (next < 0) break;

                used.Add(next);
                ordered.Add(infos[next].Item1);

                var n0 = new PointKey(infos[next].Item2, tol);
                var n1 = new PointKey(infos[next].Item3, tol);
                curEndKey = n0.Equals(curEndKey) ? n1 : n0;
            }

            if (ordered.Count != infos.Count)
            {
                warnings.Add("Selected walls could not be ordered as a single connected chain; using input order.");
                return walls;
            }

            return ordered;
        }

        private struct PointKey : IEquatable<PointKey>
        {
            public readonly long X;
            public readonly long Y;
            public readonly long Z;

            public PointKey(XYZ p, double tol)
            {
                X = (long)Math.Round(p.X / tol);
                Y = (long)Math.Round(p.Y / tol);
                Z = (long)Math.Round(p.Z / tol);
            }

            public bool Equals(PointKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => (obj is PointKey) && Equals((PointKey)obj);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + X.GetHashCode();
                    h = h * 31 + Y.GetHashCode();
                    h = h * 31 + Z.GetHashCode();
                    return h;
                }
            }
        }

        private static Autodesk.Revit.DB.Wall? CreateOneWall(
            Document doc,
            Segment seg,
            WallType newWallType,
            bool copyVerticalConstraints,
            List<string> warnings,
            bool useZCrossV)
        {
            if (seg.NewCurve == null)
            {
                warnings.Add("Wall " + seg.SourceWall.Id.IntValue() + ": NewCurve is null.");
                return null;
            }

            // base level
            var src = seg.SourceWall;
            var baseLevelId = GetElementIdParam(src, BuiltInParameter.WALL_BASE_CONSTRAINT) ?? src.LevelId;
            var baseLevel = doc.GetElement(baseLevelId) as Level;
            if (baseLevel == null)
            {
                warnings.Add("Wall " + src.Id.IntValue() + ": base level not found.");
                return null;
            }

            // base offset
            double baseOffset = GetDoubleParam(src, BuiltInParameter.WALL_BASE_OFFSET) ?? 0.0;

            // creation height (best-effort)
            string warn;
            double height = ComputeCreationHeight(doc, src, out warn);
            if (!string.IsNullOrWhiteSpace(warn)) warnings.Add("Wall " + src.Id.IntValue() + ": " + warn);

            // flip from final curve (after possible miter)
            bool flip = DetermineFlipForDesiredExterior(seg.NewCurve, seg.NewExteriorDir, useZCrossV);
            bool structural = false;

            var newWall = Autodesk.Revit.DB.Wall.Create(
                doc,
                seg.NewCurve,
                newWallType.Id,
                baseLevel.Id,
                height,
                baseOffset,
                flip,
                structural);

            if (newWall == null)
            {
                warnings.Add("Wall " + src.Id.IntValue() + ": Wall.Create returned null.");
                return null;
            }

            if (copyVerticalConstraints)
                CopyVerticalConstraints(src, newWall, warnings);

            return newWall;
        }

        // ----------------------------
        // Wall type/section helpers
        // ----------------------------

        private static WallType? FindWallType(Document doc, string nameOrId)
        {
            if (doc == null) return null;
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var s = nameOrId.Trim();

            if (int.TryParse(s, out var idInt) && idInt > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)) as WallType;

            var types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().ToList();
            var hit = types.FirstOrDefault(t => string.Equals(t.Name, s, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;

            // "contains" fallback
            return types.FirstOrDefault(t => (t.Name ?? string.Empty).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static T ParseEnum<T>(string s, T defaultValue) where T : struct
        {
            if (string.IsNullOrWhiteSpace(s)) return defaultValue;
            if (Enum.TryParse<T>(s, true, out var v)) return v;
            return defaultValue;
        }

        private static WallSectionData GetWallSectionData(WallType wallType, bool flipped, List<string> warnings)
        {
            var data = new WallSectionData();
            data.TotalWidth = wallType != null ? wallType.Width : 0.0;

            CompoundStructure cs = null;
            try { cs = wallType != null ? wallType.GetCompoundStructure() : null; } catch { cs = null; }
            if (cs == null)
            {
                data.ExteriorShell = 0;
                data.InteriorShell = 0;
                data.CoreWidth = data.TotalWidth;
                return data;
            }

            IList<CompoundStructureLayer> layers = null;
            try { layers = cs.GetLayers(); } catch { layers = null; }
            if (layers == null || layers.Count == 0)
            {
                data.ExteriorShell = 0;
                data.InteriorShell = 0;
                data.CoreWidth = data.TotalWidth;
                return data;
            }

            double extShell = 0.0;
            double intShell = 0.0;
            double core = 0.0;

            int firstCore = -1;
            int lastCore = -1;
            try { firstCore = cs.GetFirstCoreLayerIndex(); lastCore = cs.GetLastCoreLayerIndex(); } catch { firstCore = -1; lastCore = -1; }

            if (firstCore < 0 || lastCore < 0 || lastCore < firstCore || firstCore >= layers.Count)
            {
                core = data.TotalWidth;
            }
            else
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    double w = 0.0;
                    try { w = cs.GetLayerWidth(i); } catch { try { w = layers[i].Width; } catch { w = 0.0; } }
                    if (i < firstCore) extShell += w;
                    else if (i > lastCore) intShell += w;
                    else core += w;
                }
            }

            if (flipped)
            {
                var tmp = extShell;
                extShell = intShell;
                intShell = tmp;
            }

            data.ExteriorShell = extShell;
            data.InteriorShell = intShell;
            data.CoreWidth = core;

            if (data.TotalWidth <= 0)
                warnings.Add("WallType '" + (wallType != null ? wallType.Name : "(null)") + "': Width <= 0.");

            return data;
        }

        private static double GetSignedDistanceFromCenterline(WallSectionData data, PlaneRef plane, WallSide side)
        {
            var half = 0.5 * data.TotalWidth;
            switch (plane)
            {
                case PlaneRef.WallCenterline:
                    return 0.0;
                case PlaneRef.CoreCenterline:
                    return 0.5 * (data.InteriorShell - data.ExteriorShell);
                case PlaneRef.FinishFace:
                    return (side == WallSide.Exterior) ? +half : -half;
                case PlaneRef.CoreFace:
                    var dExt = half - data.ExteriorShell;
                    var dInt = half - data.InteriorShell;
                    return (side == WallSide.Exterior) ? +dExt : -dInt;
                default:
                    return 0.0;
            }
        }

        // ----------------------------
        // Side selection / normals
        // ----------------------------

        private static bool TryGetWallCurve(Autodesk.Revit.DB.Wall w, out Curve curve)
        {
            curve = null;
            var lc = w != null ? (w.Location as LocationCurve) : null;
            if (lc == null || lc.Curve == null) return false;
            curve = lc.Curve;
            return true;
        }

        private static WallSide DetermineSourceContactSide(Autodesk.Revit.DB.Wall wall, SideMode mode, XYZ globalDir, XYZ sourceExteriorDir, List<string> warnings)
        {
            if (mode == SideMode.ByExterior) return WallSide.Exterior;
            if (mode == SideMode.ByInterior) return WallSide.Interior;

            // ByGlobalDirection
            if (TryGetFinishFaceNormal2D(wall, WallSide.Exterior, out var nExt) &&
                TryGetFinishFaceNormal2D(wall, WallSide.Interior, out var nInt))
            {
                return (nExt.DotProduct(globalDir) >= nInt.DotProduct(globalDir)) ? WallSide.Exterior : WallSide.Interior;
            }

            // fallback by exterior dir
            return (sourceExteriorDir.DotProduct(globalDir) >= 0) ? WallSide.Exterior : WallSide.Interior;
        }

        private static XYZ GetPlacementDir(Autodesk.Revit.DB.Wall wall, WallSide contactSide, XYZ sourceExteriorDir, List<string> warnings)
        {
            if (TryGetFinishFaceNormal2D(wall, contactSide, out var n)) return n;
            warnings.Add("Wall " + wall.Id.IntValue() + ": failed to get face normal; using Orientation fallback.");
            return (contactSide == WallSide.Exterior) ? sourceExteriorDir : Neg(sourceExteriorDir);
        }

        private static bool TryGetFinishFaceNormal2D(Autodesk.Revit.DB.Wall wall, WallSide side, out XYZ normal2D)
        {
            normal2D = XYZ.Zero;
            if (wall == null) return false;

            var shell = (side == WallSide.Exterior) ? ShellLayerType.Exterior : ShellLayerType.Interior;
            IList<Reference> refs = null;
            try { refs = HostObjectUtils.GetSideFaces(wall, shell); } catch { refs = null; }
            if (refs == null || refs.Count == 0) return false;

            Face bestFace = null;
            double bestArea = -1;
            foreach (var r in refs)
            {
                if (r == null) continue;
                Face f = null;
                try { f = wall.GetGeometryObjectFromReference(r) as Face; } catch { f = null; }
                if (f == null) continue;

                // Prefer planar + largest area
                double area = 0;
                try { area = f.Area; } catch { area = 0; }
                if (area > bestArea)
                {
                    bestArea = area;
                    bestFace = f;
                }
            }

            if (bestFace == null) return false;
            return TryGetFaceNormal2D(bestFace, out normal2D);
        }

        private static bool TryGetFaceNormal2D(Face face, out XYZ normal2D)
        {
            normal2D = XYZ.Zero;
            if (face == null) return false;

            try
            {
                var bbuv = face.GetBoundingBox();
                var mid = new UV(0.5 * (bbuv.Min.U + bbuv.Max.U), 0.5 * (bbuv.Min.V + bbuv.Max.V));
                var n = face.ComputeNormal(mid);
                normal2D = Normalize2D(n);
                return !normal2D.IsZeroLength();
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------
        // Curve offset & miter
        // ----------------------------

        private static Curve MakeOffsetCurve(Curve src, XYZ placementDir, double offset, List<string> warnings)
        {
            if (src == null) return null;
            if (Math.Abs(offset) < 1e-7) return src.Clone();

            var dist = Math.Abs(offset);
            Curve c1 = null;
            Curve c2 = null;
            try { c1 = src.CreateOffset(dist, XYZ.BasisZ); } catch (Exception ex) { warnings.Add("CreateOffset(+" + dist + ") failed: " + ex.Message); }
            try { c2 = src.CreateOffset(-dist, XYZ.BasisZ); } catch (Exception ex) { warnings.Add("CreateOffset(-" + dist + ") failed: " + ex.Message); }

            if (c1 == null && c2 == null) return src.Clone();
            if (c1 != null && c2 == null) return c1;
            if (c1 == null && c2 != null) return c2;

            var m0 = src.Evaluate(0.5, true);
            var m1 = c1.Evaluate(0.5, true);
            var m2 = c2.Evaluate(0.5, true);
            var d1 = (m1 - m0).DotProduct(placementDir);
            var d2 = (m2 - m0).DotProduct(placementDir);
            return offset > 0 ? (d1 >= d2 ? c1 : c2) : (d1 <= d2 ? c1 : c2);
        }

        private static void ApplyMiterForLineChain(List<Segment> segs, List<string> warnings)
        {
            for (int i = 0; i < segs.Count - 1; i++)
            {
                var a = segs[i].NewCurve as Line;
                var b = segs[i + 1].NewCurve as Line;
                if (a == null || b == null) continue;
                if (!TryIntersectInfiniteLines2D(a, b, out var p)) continue;

                var a0 = a.GetEndPoint(0);
                var b1 = b.GetEndPoint(1);
                var z = 0.5 * (a.GetEndPoint(1).Z + b.GetEndPoint(0).Z);
                p = new XYZ(p.X, p.Y, z);

                segs[i].NewCurve = Line.CreateBound(a0, p);
                segs[i + 1].NewCurve = Line.CreateBound(p, b1);
            }
        }

        private static bool TryIntersectInfiniteLines2D(Line a, Line b, out XYZ intersection)
        {
            intersection = XYZ.Zero;
            var p = a.GetEndPoint(0);
            var r = a.Direction;
            var q = b.GetEndPoint(0);
            var s = b.Direction;

            var rxs = Cross2D(r, s);
            if (Math.Abs(rxs) < 1e-12) return false;

            var t = Cross2D(q - p, s) / rxs;
            var i = p + t * r;
            intersection = new XYZ(i.X, i.Y, 0);
            return true;
        }

        private static double Cross2D(XYZ a, XYZ b) => a.X * b.Y - a.Y * b.X;

        // ----------------------------
        // Vertical constraints copy (and height inference)
        // ----------------------------

        private static void CopyVerticalConstraints(Autodesk.Revit.DB.Wall src, Autodesk.Revit.DB.Wall dst, List<string> warnings)
        {
            CopyParamElementId(src, dst, BuiltInParameter.WALL_BASE_CONSTRAINT, warnings);
            CopyParamDouble(src, dst, BuiltInParameter.WALL_BASE_OFFSET, warnings);

            var top = GetElementIdParam(src, BuiltInParameter.WALL_HEIGHT_TYPE);
            bool hasTop = false;
            try { if (top != null && top != ElementId.InvalidElementId && dst.Document.GetElement(top) is Level) hasTop = true; } catch { hasTop = false; }

            if (hasTop)
            {
                SetParamElementId(dst, BuiltInParameter.WALL_HEIGHT_TYPE, top, warnings);
                CopyParamDouble(src, dst, BuiltInParameter.WALL_TOP_OFFSET, warnings);
            }
            else
            {
                CopyParamDouble(src, dst, BuiltInParameter.WALL_USER_HEIGHT_PARAM, warnings);
            }
        }

        private static double ComputeCreationHeight(Document doc, Autodesk.Revit.DB.Wall w, out string warn)
        {
            warn = null;

            var h = GetDoubleParam(w, BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (h.HasValue && h.Value > 1e-6) return h.Value;

            var baseId = GetElementIdParam(w, BuiltInParameter.WALL_BASE_CONSTRAINT);
            var topId = GetElementIdParam(w, BuiltInParameter.WALL_HEIGHT_TYPE);
            var baseOff = GetDoubleParam(w, BuiltInParameter.WALL_BASE_OFFSET) ?? 0.0;
            var topOff = GetDoubleParam(w, BuiltInParameter.WALL_TOP_OFFSET) ?? 0.0;

            if (baseId != null && topId != null)
            {
                var baseLv = doc.GetElement(baseId) as Level;
                var topLv = doc.GetElement(topId) as Level;
                if (baseLv != null && topLv != null)
                {
                    var hh = (topLv.Elevation + topOff) - (baseLv.Elevation + baseOff);
                    if (hh > 1e-6) return hh;
                }
            }

            try
            {
                var bb = w.get_BoundingBox(null);
                if (bb != null)
                {
                    var hh = bb.Max.Z - bb.Min.Z;
                    if (hh > 1e-6) return hh;
                }
            }
            catch { }

            warn = "Failed to infer height; using 10ft.";
            return 10.0;
        }

        private static ElementId? GetElementIdParam(Element e, BuiltInParameter bip)
        {
            try
            {
                var p = e?.get_Parameter(bip);
                if (p == null) return null;
                if (p.StorageType != StorageType.ElementId) return null;
                var id = p.AsElementId();
                return (id == null || id == ElementId.InvalidElementId) ? null : id;
            }
            catch { return null; }
        }

        private static double? GetDoubleParam(Element e, BuiltInParameter bip)
        {
            try
            {
                var p = e?.get_Parameter(bip);
                if (p == null) return null;
                if (p.StorageType != StorageType.Double) return null;
                return p.AsDouble();
            }
            catch { return null; }
        }

        private static void CopyParamElementId(Element src, Element dst, BuiltInParameter bip, List<string> warnings)
        {
            var id = GetElementIdParam(src, bip);
            if (id == null) return;
            SetParamElementId(dst, bip, id, warnings);
        }

        private static void CopyParamDouble(Element src, Element dst, BuiltInParameter bip, List<string> warnings)
        {
            var v = GetDoubleParam(src, bip);
            if (!v.HasValue) return;
            SetParamDouble(dst, bip, v.Value, warnings);
        }

        private static void SetParamElementId(Element dst, BuiltInParameter bip, ElementId? id, List<string> warnings)
        {
            if (dst == null || id == null) return;
            try
            {
                var p = dst.get_Parameter(bip);
                if (p == null) return;
                if (p.IsReadOnly) { warnings.Add("Param " + bip + ": read-only."); return; }
                p.Set(id);
            }
            catch (Exception ex) { warnings.Add("Param " + bip + " set failed: " + ex.Message); }
        }

        private static void SetParamDouble(Element dst, BuiltInParameter bip, double v, List<string> warnings)
        {
            try
            {
                var p = dst.get_Parameter(bip);
                if (p == null) return;
                if (p.IsReadOnly) { warnings.Add("Param " + bip + ": read-only."); return; }
                p.Set(v);
            }
            catch (Exception ex) { warnings.Add("Param " + bip + " set failed: " + ex.Message); }
        }

        // ----------------------------
        // Curve direction -> flip helpers
        // ----------------------------

        private static XYZ DetermineNewExteriorDir(NewExteriorMode mode, XYZ placementDir, XYZ sourceExteriorDir)
        {
            switch (mode)
            {
                case NewExteriorMode.AwayFromSource:
                    return placementDir;
                case NewExteriorMode.OppositeSourceExterior:
                    return Neg(sourceExteriorDir);
                case NewExteriorMode.MatchSourceExterior:
                default:
                    return sourceExteriorDir;
            }
        }

        private static bool CalibrateZCrossVConvention(Autodesk.Revit.DB.Wall wall, List<string> warnings)
        {
            if (!TryGetWallCurve(wall, out var c)) return true;
            var v = GetCurveTangent2D(c);
            if (v.IsZeroLength()) return true;

            var wZxV = Normalize2D(XYZ.BasisZ.CrossProduct(v));
            if (wall.Flipped) wZxV = Neg(wZxV);

            var o = Normalize2D(wall.Orientation);
            if (o.IsZeroLength()) return true;

            bool ok = (wZxV.DotProduct(o) >= 0);
            if (!ok) warnings.Add("Exterior direction convention: using v.Cross(Z) instead of Z.Cross(v).");
            return ok;
        }

        private static bool DetermineFlipForDesiredExterior(Curve curve, XYZ desiredExteriorDir, bool useZCrossV)
        {
            var unflipped = ComputeUnflippedExteriorDirFromCurve(curve, useZCrossV);
            if (unflipped.IsZeroLength()) return false;
            return (unflipped.DotProduct(desiredExteriorDir) < 0);
        }

        private static XYZ ComputeUnflippedExteriorDirFromCurve(Curve curve, bool useZCrossV)
        {
            var v = GetCurveTangent2D(curve);
            if (v.IsZeroLength()) return XYZ.Zero;
            var w = useZCrossV ? XYZ.BasisZ.CrossProduct(v) : v.CrossProduct(XYZ.BasisZ);
            return Normalize2D(w);
        }

        private static XYZ GetCurveTangent2D(Curve c)
        {
            try
            {
                var deriv = c.ComputeDerivatives(0.5, true);
                return Normalize2D(deriv.BasisX);
            }
            catch
            {
                try
                {
                    var p0 = c.GetEndPoint(0);
                    var p1 = c.GetEndPoint(1);
                    return Normalize2D(p1 - p0);
                }
                catch
                {
                    return XYZ.Zero;
                }
            }
        }

        private static XYZ ToXyz(double[]? v, XYZ fallback)
        {
            try
            {
                if (v == null || v.Length < 2) return fallback;
                var x = v.Length > 0 ? v[0] : fallback.X;
                var y = v.Length > 1 ? v[1] : fallback.Y;
                var z = v.Length > 2 ? v[2] : fallback.Z;
                return new XYZ(x, y, z);
            }
            catch { return fallback; }
        }

        private static XYZ Normalize2D(XYZ v)
        {
            if (v == null) return XYZ.Zero;
            var vv = new XYZ(v.X, v.Y, 0);
            var len = vv.GetLength();
            if (len < 1e-12) return XYZ.Zero;
            return vv / len;
        }

        private static XYZ Neg(XYZ v) => v == null ? XYZ.Zero : new XYZ(-v.X, -v.Y, -v.Z);

        private static bool IsZeroLength(this XYZ v) => v == null || v.GetLength() < 1e-12;
    }
}
