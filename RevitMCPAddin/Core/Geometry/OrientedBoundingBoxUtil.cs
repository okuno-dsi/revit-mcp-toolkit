// ================================================================
// File: Core/Geometry/OrientedBoundingBoxUtil.cs  (robust, Fx 4.8)
// Purpose:
//   - Oriented Bounding Box 計算（∞初期化の廃止 / 最初の頂点で初期化）
//   - 構造フレーム/ブレース等は LocationCurve 主軸（strategy:"framing"）
//   - Transform 直交正規化 & 可逆チェック、NaN/∞ スキップ
// Target: .NET Framework 4.8 / Revit 2023+
// Depends: Core/Geometry/OrientedBoundingBox.cs (Point3D/DTO)
// ================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core.Geometry
{
    internal static class OrientedBoundingBoxUtil
    {
        public static ObbResponse TryCompute(
            Document doc, ElementId id,
            string strategy = "auto",
            string detailLevel = "fine",
            bool includeCorners = true)
        {
            var resp = new ObbResponse { Ok = false, Msg = "" };
            if (doc == null) { resp.Msg = "Document is null."; return resp; }
            var e = doc.GetElement(id);
            if (e == null) { resp.Msg = $"Element not found: {id.IntValue()}"; return resp; }

            // 1) 基底推定（堅牢）
            var basis = BuildLocalBasisTransformRobust(e, strategy ?? "auto", out string basisNote);
            if (basis == null || !IsInvertible(basis))
            {
                resp.Msg = "要素のローカル基準を決められません（Transform未取得/非可逆）。strategy='framing' や 'plane2d' を試してください。";
                return resp;
            }

            // 2) Geometry
            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = (detailLevel ?? "fine").ToLower() == "coarse" ? ViewDetailLevel.Coarse
                           : (detailLevel ?? "fine").ToLower() == "medium" ? ViewDetailLevel.Medium
                           : ViewDetailLevel.Fine
            };
            var ge = e.get_Geometry(opts);
            if (ge == null) { resp.Msg = "ジオメトリが取得できません。"; return resp; }

            // 3) 頂点列挙 → local に落として min/max
            var toLocal = basis.Inverse;
            bool inited = false; // ★ 最初の点で初期化
            XYZ min = null, max = null;

            void Accumulate(XYZ world)
            {
                if (!IsFinite(world)) return;
                XYZ p;
                try { p = toLocal.OfPoint(world); }
                catch { return; }
                if (!IsFinite(p)) return;

                if (!inited)
                {
                    min = p; max = p;
                    inited = true;
                    return;
                }

                if (p.X < min.X) min = new XYZ(p.X, min.Y, min.Z);
                if (p.Y < min.Y) min = new XYZ(min.X, p.Y, min.Z);
                if (p.Z < min.Z) min = new XYZ(min.X, min.Y, p.Z);

                if (p.X > max.X) max = new XYZ(p.X, max.Y, max.Z);
                if (p.Y > max.Y) max = new XYZ(max.X, p.Y, max.Z);
                if (p.Z > max.Z) max = new XYZ(max.X, max.Y, p.Z);
            }

            EnumerateVerticesRobust(ge, Transform.Identity, strategy, Accumulate);
            if (!inited) { resp.Msg = "ジオメトリに有効な頂点が見つかりません（空/非対応/非有限）。"; return resp; }

            // 4) ローカルで center/extents → world
            var centerLocal = (min + max) * 0.5;
            var extents = (max - min) * 0.5;

            var ax = NormalizeSafe(basis.BasisX);
            var ay = NormalizeSafe(basis.BasisY);
            var az = NormalizeSafe(basis.BasisZ);
            Orthonormalize(ref ax, ref ay, ref az);

            var centerWorld = basis.OfPoint(centerLocal);

            var dto = new OrientedBoundingBoxDto
            {
                Center = ToP3(centerWorld),
                AxisX = ToP3(ax),
                AxisY = ToP3(ay),
                AxisZ = ToP3(az),
                ExtentX = extents.X,
                ExtentY = extents.Y,
                ExtentZ = extents.Z,
                Volume = Math.Abs(8.0 * extents.X * extents.Y * extents.Z),
                Notes = basisNote
            };

            if (includeCorners)
            {
                var sgn = new[] { -1, 1 };
                foreach (var sx in sgn)
                    foreach (var sy in sgn)
                        foreach (var sz in sgn)
                        {
                            var local = new XYZ(centerLocal.X + sx * extents.X,
                                                centerLocal.Y + sy * extents.Y,
                                                centerLocal.Z + sz * extents.Z);
                            var w = basis.OfPoint(local);
                            if (IsFinite(w)) dto.Corners.Add(ToP3(w));
                        }
            }

            resp.Ok = true;
            resp.Obb = dto;
            return resp;
        }

        // -------- Geometry enumeration (robust) --------
        private static void EnumerateVerticesRobust(GeometryElement ge, Transform world, string strategy, Action<XYZ> add)
        {
            foreach (var obj in ge)
            {
                if (obj is GeometryInstance gi)
                {
                    var t = world.Multiply(gi.Transform);
                    var sg = gi.GetSymbolGeometry();
                    if (sg != null) EnumerateVerticesRobust(sg, t, strategy, add);
                }
                else if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        IList<XYZ> pts = null;
                        try { pts = edge.Tessellate(); } catch { }
                        if (pts == null) continue;
                        foreach (var p in pts) { var wp = world.OfPoint(p); if (IsFinite(wp)) add(wp); }
                    }

                    var s = (strategy ?? "").ToLower();
                    if (s == "faces" || s == "tri")
                    {
                        foreach (Face face in solid.Faces)
                        {
                            Mesh mesh = null;
                            try { mesh = face.Triangulate(); } catch { }
                            if (mesh == null) continue;
                            var verts = mesh.Vertices;
                            if (verts == null) continue;
                            foreach (var v in verts) { var wp = world.OfPoint(v); if (IsFinite(wp)) add(wp); }
                        }
                    }
                }
                else if (obj is Mesh mesh)
                {
                    var verts = mesh.Vertices;
                    if (verts != null)
                        foreach (var v in verts) { var wp = world.OfPoint(v); if (IsFinite(wp)) add(wp); }
                }
            }
        }

        // -------- Basis estimation (robust) --------
        private static Transform BuildLocalBasisTransformRobust(Element e, string strategy, out string note)
        {
            note = "";
            var s = (strategy ?? "auto").ToLower();

            // 0) FamilyInstance + LocationCurve（梁/ブレース安定）
            if (e is FamilyInstance fiLC && (s == "auto" || s == "framing"))
            {
                var lc = fiLC.Location as LocationCurve;
                var crv = lc?.Curve;
                if (crv != null)
                {
                    var t0 = MakeBasisFromCurve(crv, out string n);
                    if (t0 != null && IsInvertible(t0)) { note = "basis: LocationCurve (framing) – " + n; return t0; }
                }
                if (s == "framing") note = "framing指定だがLocationCurveなし → fallback";
            }

            // 1) FamilyInstance.Transform（直交正規化）
            if (e is FamilyInstance fi && (s == "auto" || s == "family"))
            {
                var t = fi.GetTransform();
                if (t != null)
                {
                    var ortho = OrthonormalizeTransform(t, out bool okOrtho);
                    if (okOrtho && IsInvertible(ortho)) { note = "basis: FamilyInstance.GetTransform() (orthonormalized)"; return ortho; }
                }
            }

            // 2) 壁: 位置線
            if (e is Wall wall && (s == "auto" || s == "wall"))
            {
                var lc = wall.Location as LocationCurve;
                var crv = lc?.Curve;
                if (crv != null)
                {
                    var t = MakeBasisFromCurve(crv, out string n);
                    if (t != null && IsInvertible(t)) { note = "basis: Wall LocationCurve – " + n; return t; }
                }
            }

            // 3) 平面要素: XY PCA
            if (s == "plane2d" || s == "pca2d" || s == "auto")
            {
                var basis = EstimatePlanarBasisXY(e, out string msg);
                if (basis != null && IsInvertible(basis)) { note = msg; return basis; }
                if (s != "auto") { note = "plane2d/pca2d basis 推定に失敗"; return null; }
            }

            // 4) Fallbacks
            if (e.Location is LocationPoint lp)
            {
                var t = Transform.Identity; t.Origin = lp.Point;
                note = "basis: fallback World axes @ LocationPoint"; return t;
            }
            var bbox = e.get_BoundingBox(null);
            if (bbox != null)
            {
                var t = Transform.Identity; t.Origin = (bbox.Min + bbox.Max) * 0.5;
                note = "basis: fallback World axes @ element AABB center"; return t;
            }
            return null;
        }

        private static Transform MakeBasisFromCurve(Curve crv, out string note)
        {
            note = "curve";
            try
            {
                var start = crv.GetEndPoint(0);
                var end = crv.GetEndPoint(1);
                var dir = end - start;
                if (dir.GetLength() < 1e-12) return null;

                var x = NormalizeSafe(dir);
                var z = XYZ.BasisZ;
                if (Math.Abs(x.DotProduct(z)) > 0.999) x = XYZ.BasisX;
                var y = NormalizeSafe(z.CrossProduct(x));
                z = NormalizeSafe(x.CrossProduct(y));
                Orthonormalize(ref x, ref y, ref z);

                var origin = crv.Evaluate(0.5, true);
                var t = Transform.Identity;
                t.Origin = origin;
                t.BasisX = x; t.BasisY = y; t.BasisZ = z;
                note = crv.GetType().Name;
                return t;
            }
            catch { return null; }
        }

        private static Transform EstimatePlanarBasisXY(Element e, out string note)
        {
            note = "";
            try
            {
                var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Coarse };
                var ge = e.get_Geometry(opts);
                if (ge == null) return null;

                var pts = new List<XYZ>(1024);
                void EnumVerts(GeometryElement g, Transform w)
                {
                    foreach (var obj in g)
                    {
                        if (obj is GeometryInstance gi)
                        {
                            var t = w.Multiply(gi.Transform);
                            var sg = gi.GetSymbolGeometry();
                            if (sg != null) EnumVerts(sg, t);
                        }
                        else if (obj is Solid sld && sld.Volume > 1e-9)
                        {
                            foreach (Edge ed in sld.Edges)
                            {
                                IList<XYZ> vs = null;
                                try { vs = ed.Tessellate(); } catch { }
                                if (vs == null) continue;
                                foreach (var p in vs) { var wp = w.OfPoint(p); if (IsFinite(wp)) pts.Add(wp); }
                            }
                        }
                        else if (obj is Mesh m)
                        {
                            var verts = m.Vertices;
                            if (verts != null)
                                foreach (var v in verts) { var wp = w.OfPoint(v); if (IsFinite(wp)) pts.Add(wp); }
                        }
                    }
                }
                EnumVerts(ge, Transform.Identity);
                if (pts.Count < 3) return null;

                double cx = 0, cy = 0, cz = 0;
                foreach (var p in pts) { cx += p.X; cy += p.Y; cz += p.Z; }
                cx /= pts.Count; cy /= pts.Count; cz /= pts.Count;

                double a = 0, b = 0, c = 0;
                foreach (var p in pts)
                {
                    double dx = p.X - cx, dy = p.Y - cy;
                    a += dx * dx; b += dx * dy; c += dy * dy;
                }
                a /= pts.Count; b /= pts.Count; c /= pts.Count;

                double tvar = Math.Sqrt((a - c) * (a - c) + 4 * b * b);
                double l1 = 0.5 * (a + c + tvar);
                var vx = new XYZ(b, (l1 - a), 0.0);
                if (vx.GetLength() < 1e-12) vx = XYZ.BasisX;
                vx = NormalizeSafe(vx);
                var vz = XYZ.BasisZ;
                if (Math.Abs(vx.DotProduct(vz)) > 0.999) vx = XYZ.BasisX;
                var vy = NormalizeSafe(vz.CrossProduct(vx));
                vz = NormalizeSafe(vx.CrossProduct(vy));
                Orthonormalize(ref vx, ref vy, ref vz);

                var tf = Transform.Identity;
                tf.Origin = new XYZ(cx, cy, cz);
                tf.BasisX = vx; tf.BasisY = vy; tf.BasisZ = vz;
                note = "basis: PCA 2D (XY) from edges";
                return tf;
            }
            catch { return null; }
        }

        // ---- Transform utilities ----
        private static Transform OrthonormalizeTransform(Transform t, out bool ok)
        {
            ok = false;
            try
            {
                var x = NormalizeSafe(t.BasisX);
                var y = NormalizeSafe(t.BasisY);
                var z = NormalizeSafe(t.BasisZ);
                Orthonormalize(ref x, ref y, ref z);

                var rt = Transform.Identity;
                rt.Origin = t.Origin;
                rt.BasisX = x; rt.BasisY = y; rt.BasisZ = z;

                ok = IsInvertible(rt);
                return rt;
            }
            catch { ok = false; return t; }
        }

        private static void Orthonormalize(ref XYZ x, ref XYZ y, ref XYZ z)
        {
            x = NormalizeSafe(x);
            y = NormalizeSafe(y - x.Multiply(x.DotProduct(y)));
            z = NormalizeSafe(x.CrossProduct(y));
            y = NormalizeSafe(z.CrossProduct(x));
        }

        private static bool IsInvertible(Transform t)
        {
            var det = t.BasisX.DotProduct(t.BasisY.CrossProduct(t.BasisZ));
            return IsFiniteNumber(det) && Math.Abs(det) > 1e-9;
        }

        // ---- helpers ----
        private static Point3D ToP3(XYZ p) => new Point3D(p.X, p.Y, p.Z);

        private static XYZ NormalizeSafe(XYZ v)
        {
            var len = v.GetLength();
            if (!IsFiniteNumber(len) || len < 1e-12) return v;
            return new XYZ(v.X / len, v.Y / len, v.Z / len);
        }

        private static bool IsFiniteNumber(double d) => !(double.IsNaN(d) || double.IsInfinity(d));
        private static bool IsFinite(XYZ p) => IsFiniteNumber(p.X) && IsFiniteNumber(p.Y) && IsFiniteNumber(p.Z);
    }
}

