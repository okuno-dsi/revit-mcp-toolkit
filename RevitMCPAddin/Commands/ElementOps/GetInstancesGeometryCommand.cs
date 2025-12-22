// File: Commands/ElementOps/GetInstancesGeometryCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    /// <summary>
    /// Batch geometry export for multiple elements across categories.
    /// - Accepts elementIds[], uniqueIds[], or fromSelection=true
    /// - Returns per-element triangulated mesh (same shape as get_instance_geometry)
    /// - Supports paging via page.startIndex/page.batchSize
    /// </summary>
    public class GetInstancesGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_instances_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var uidoc = uiapp?.ActiveUIDocument;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = cmd.Params as JObject ?? new JObject();

            // Collect target ids
            var ids = new List<ElementId>();

            if (p.TryGetValue("elementIds", out var arrTok) && arrTok is JArray arr)
            {
                foreach (var t in arr) { try { ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(t.Value<int>())); } catch { } }
            }

            if (p.TryGetValue("uniqueIds", out var uArrTok) && uArrTok is JArray uarr)
            {
                foreach (var u in uarr)
                {
                    try
                    {
                        var e = doc.GetElement(u.Value<string>());
                        if (e != null) ids.Add(e.Id);
                    }
                    catch { }
                }
            }

            bool fromSelection = p.Value<bool?>("fromSelection") ?? false;
            if (fromSelection && uidoc != null)
            {
                try { ids.AddRange(uidoc.Selection.GetElementIds()); } catch { }
            }

            // Distinct & slice for paging
            ids = ids.Where(x => x != null && x != ElementId.InvalidElementId)
                     .Distinct(new ElementIdComparer())
                     .ToList();

            int startIndex = Math.Max(0, p.SelectToken("page.startIndex")?.Value<int?>() ?? 0);
            int batchSize = Math.Max(1, p.SelectToken("page.batchSize")?.Value<int?>() ?? (ids.Count > 0 ? ids.Count : 1));
            var slice = ids.Skip(startIndex).Take(batchSize).ToList();

            // Options
            var detail = ParseDetailLevel(p.Value<string>("detailLevel")); // "Coarse"|"Medium"|"Fine"
            bool includeNonVisible = p.Value<bool?>("includeNonVisible") ?? false;
            bool weld = p.Value<bool?>("weld") ?? true;
            double tolFt = p.Value<double?>("weldTolerance") ?? 1e-6; // feet
            // If mm tolerance is provided, prefer it
            double tolMm = p.Value<double?>("weldToleranceMm") ?? -1;
            if (tolMm > 0) tolFt = tolMm / 304.8; // mm -> ft
            bool includeAnalytic = p.Value<bool?>("includeAnalytic") ?? false;

            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = includeNonVisible,
                DetailLevel = detail
            };

            var items = new List<object>(slice.Count);
            foreach (var id in slice)
            {
                try
                {
                    var e = doc.GetElement(id);
                    if (e == null)
                    {
                        items.Add(new { ok = false, elementId = id.IntValue(), error = "not_found" });
                        continue;
                    }

                    // Optional analytic fallback (room contours / location curve / point)
                    object analytic = null;
                    if (includeAnalytic)
                    {
                        analytic = BuildAnalytic(doc, e);
                    }

                    var ge = e.get_Geometry(opts);
                    bool hasGeom = ge != null;
                    if (!hasGeom && analytic != null)
                    {
                        // Return analytic-only record
                        var et0 = doc.GetElement(e.GetTypeId()) as ElementType;
                        items.Add(new
                        {
                            ok = true,
                            elementId = e.Id.IntValue(),
                            uniqueId = e.UniqueId,
                            category = e.Category?.Name,
                            typeName = et0?.Name,
                            units = "feet",
                            vertices = (object)null,
                            normals = (object)null,
                            uvs = (object)null,
                            submeshes = (object)null,
                            materials = (object)null,
                            analytic = analytic
                        });
                        continue;
                    }
                    if (!hasGeom)
                    {
                        items.Add(new { ok = false, elementId = id.IntValue(), error = "no_geometry" });
                        continue;
                    }

                    var collector = new MeshCollector(weld, tolFt);
                    var rootT = GetElementRootTransform(e);
                    try { TraverseGeometry(ge, rootT, doc, collector); }
                    catch (Exception ex)
                    {
                        items.Add(new { ok = false, elementId = id.IntValue(), error = "traverse_failed: " + ex.Message });
                        continue;
                    }

                    if (collector.TotalTriangleCount == 0 && collector.TotalVertexCount == 0)
                    {
                        items.Add(new { ok = false, elementId = id.IntValue(), error = "empty" });
                        continue;
                    }

                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    items.Add(new
                    {
                        ok = true,
                        elementId = e.Id.IntValue(),
                        uniqueId = e.UniqueId,
                        category = e.Category?.Name,
                        typeName = et?.Name,
                        units = "feet",
                        transform = ToArray4x4(rootT),
                        vertices = collector.BuildVertexArray(),
                        normals = collector.BuildNormalArrayOrNull(),
                        uvs = collector.BuildUvArrayOrNull(),
                        submeshes = collector.BuildSubmeshIndexArrays(),
                        materials = collector.BuildMaterialArray(doc),
                        analytic = analytic
                    });
                }
                catch (Exception ex)
                {
                    items.Add(new { ok = false, elementId = id?.IntValue() ?? -1, error = ex.Message });
                }
            }

            int total = ids.Count;
            int next = startIndex + slice.Count;
            bool completed = next >= total;
            return new { ok = true, items, nextIndex = completed ? (int?)null : next, completed, totalCount = total };
        }

        // ----------------- Analytic helpers (mm) -----------------
        private static object BuildAnalytic(Document doc, Element e)
        {
            try
            {
                // Rooms
                if (e is Autodesk.Revit.DB.Architecture.Room room)
                {
                    var opts = new SpatialElementBoundaryOptions();
                    var loops = room.GetBoundarySegments(opts);
                    if (loops == null || loops.Count == 0) return null;
                    var contours = new List<object>();
                    foreach (IList<BoundarySegment> loop in loops)
                    {
                        var pts = new List<object>();
                        foreach (var seg in loop)
                        {
                            var c = seg?.GetCurve();
                            if (c == null) continue;
                            try
                            {
                                var tess = c.Tessellate();
                                if (tess != null && tess.Count > 0)
                                {
                                    foreach (var p in tess)
                                        pts.Add(PtMm(p));
                                }
                            }
                            catch { }
                        }
                        if (pts.Count > 1) contours.Add(pts);
                    }
                    if (contours.Count > 0) return new { kind = "room", contours };
                }

                // Spaces
                if (e is Autodesk.Revit.DB.Mechanical.Space space)
                {
                    var opts = new SpatialElementBoundaryOptions();
                    var loops = space.GetBoundarySegments(opts);
                    if (loops == null || loops.Count == 0) return null;
                    var contours = new List<object>();
                    foreach (IList<BoundarySegment> loop in loops)
                    {
                        var pts = new List<object>();
                        foreach (var seg in loop)
                        {
                            var c = seg?.GetCurve();
                            if (c == null) continue;
                            try
                            {
                                var tess = c.Tessellate();
                                if (tess != null && tess.Count > 0)
                                {
                                    foreach (var p in tess)
                                        pts.Add(PtMm(p));
                                }
                            }
                            catch { }
                        }
                        if (pts.Count > 1) contours.Add(pts);
                    }
                    if (contours.Count > 0) return new { kind = "space", contours };
                }

                // LocationCurve (e.g., beams/columns/walls baselines)
                if (e.Location is LocationCurve lc && lc.Curve != null)
                {
                    var a = lc.Curve.GetEndPoint(0);
                    var b = lc.Curve.GetEndPoint(1);
                    return new { kind = "curve", wire = new { a = PtMm(a), b = PtMm(b) } };
                }

                // LocationPoint
                if (e.Location is LocationPoint lp && lp.Point != null)
                {
                    return new { kind = "point", point = PtMm(lp.Point) };
                }
            }
            catch { }
            return null;
        }

        private static object PtMm(XYZ p)
        {
            double mmX = UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters);
            double mmY = UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters);
            double mmZ = UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters);
            return new { x = Math.Round(mmX, 3), y = Math.Round(mmY, 3), z = Math.Round(mmZ, 3) };
        }

        private static ViewDetailLevel ParseDetailLevel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ViewDetailLevel.Fine;
            s = s.Trim().ToLowerInvariant();
            if (s.StartsWith("c")) return ViewDetailLevel.Coarse;
            if (s.StartsWith("m")) return ViewDetailLevel.Medium;
            return ViewDetailLevel.Fine;
        }

        private static Transform GetElementRootTransform(Element e)
        {
            try
            {
                if (e is FamilyInstance fi)
                {
                    var t = fi.GetTransform();
                    if (t != null) return t;
                }
            }
            catch { }
            return Transform.Identity;
        }

        private static void TraverseGeometry(GeometryElement ge, Transform current, Document doc, MeshCollector collector)
        {
            foreach (var obj in ge)
            {
                if (obj is GeometryInstance gi)
                {
                    var inst = gi.GetInstanceGeometry();
                    var t = gi.Transform != null ? current.Multiply(gi.Transform) : current;
                    TraverseGeometry(inst, t, doc, collector);
                }
                else if (obj is Solid solid)
                {
                    AddSolid(solid, current, doc, collector);
                }
                else if (obj is Mesh m)
                {
                    AddMesh(m, current, ElementId.InvalidElementId, collector);
                }
            }
        }

        private static void AddSolid(Solid solid, Transform t, Document doc, MeshCollector collector)
        {
            if (solid == null || solid.Faces == null || solid.Faces.Size == 0) return;
            foreach (Autodesk.Revit.DB.Face f in solid.Faces)
            {
                Mesh m = null;
                try { m = f.Triangulate(); } catch { continue; }
                if (m == null || m.NumTriangles == 0) continue;
                var matId = (f.MaterialElementId != null && f.MaterialElementId.IntValue() > 0) ? f.MaterialElementId : ElementId.InvalidElementId;
                AddMesh(m, t, matId, collector);
            }
        }

        private static void AddMesh(Mesh m, Transform t, ElementId matId, MeshCollector collector)
        {
            int vCount = m.Vertices.Count;
            var transformed = new XYZ[vCount];
            for (int i = 0; i < vCount; i++) transformed[i] = t.OfPoint(m.Vertices[i]);
            for (int ti = 0; ti < m.NumTriangles; ti++)
            {
                var tri = m.get_Triangle(ti);
                int a = Convert.ToInt32(tri.get_Index(0));
                int b = Convert.ToInt32(tri.get_Index(1));
                int c = Convert.ToInt32(tri.get_Index(2));
                collector.AddTriangle(matId, transformed[a], transformed[b], transformed[c]);
            }
        }

        private static double[][] ToArray4x4(Transform t)
        {
            return new double[4][]
            {
                new double[]{ t.BasisX.X, t.BasisX.Y, t.BasisX.Z, t.Origin.X },
                new double[]{ t.BasisY.X, t.BasisY.Y, t.BasisY.Z, t.Origin.Y },
                new double[]{ t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, t.Origin.Z },
                new double[]{ 0, 0, 0, 1 }
            };
        }

        private class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y) => (x?.IntValue() ?? -1) == (y?.IntValue() ?? -1);
            public int GetHashCode(ElementId obj) => obj?.IntValue().GetHashCode() ?? 0;
        }

        private class MeshCollector
        {
            private readonly bool _weld;
            private readonly double _tol;
            private readonly Dictionary<VertexKey, int> _vertexLookup = new Dictionary<VertexKey, int>();
            private readonly List<XYZ> _positions = new List<XYZ>(8192);
            private readonly List<XYZ> _accNormals = new List<XYZ>(8192);
            private readonly List<UV> _uvs = new List<UV>(8192);
            private readonly Dictionary<int, List<int>> _matToIndices = new Dictionary<int, List<int>>();
            private readonly HashSet<int> _materialIds = new HashSet<int>();

            public MeshCollector(bool weld, double weldToleranceFt)
            {
                _weld = weld;
                _tol = Math.Max(1e-9, weldToleranceFt);
            }

            public int TotalVertexCount => _positions.Count;
            public int TotalTriangleCount => _matToIndices.Values.Sum(v => v.Count / 3);

            public void AddTriangle(ElementId matId, XYZ p0, XYZ p1, XYZ p2)
            {
                int matKey = (matId != null && matId.IntValue() > 0) ? matId.IntValue() : -1;
                if (!_matToIndices.TryGetValue(matKey, out var list)) { list = new List<int>(4096); _matToIndices[matKey] = list; }
                _materialIds.Add(matKey);

                var n = FaceNormal(p0, p1, p2);
                int i0 = AddVertex(p0, n);
                int i1 = AddVertex(p1, n);
                int i2 = AddVertex(p2, n);
                list.Add(i0); list.Add(i1); list.Add(i2);
            }

            private int AddVertex(XYZ p, XYZ n)
            {
                if (_weld)
                {
                    var key = new VertexKey(p, _tol);
                    if (_vertexLookup.TryGetValue(key, out int idx))
                    {
                        _accNormals[idx] = _accNormals[idx] + n;
                        return idx;
                    }
                    int ni = _positions.Count;
                    _vertexLookup[key] = ni;
                    _positions.Add(p);
                    _accNormals.Add(n);
                    _uvs.Add(new UV(0, 0));
                    return ni;
                }
                else
                {
                    int ni = _positions.Count;
                    _positions.Add(p);
                    _accNormals.Add(n);
                    _uvs.Add(new UV(0, 0));
                    return ni;
                }
            }

            private static XYZ FaceNormal(XYZ a, XYZ b, XYZ c)
            {
                var u = b - a; var v = c - a; var n = u.CrossProduct(v);
                try { n = n.Normalize(); } catch { n = new XYZ(0, 0, 1); }
                return n;
            }

            public object BuildVertexArray()
            {
                var arr = new List<double>(_positions.Count * 3);
                foreach (var p in _positions) { arr.Add(p.X); arr.Add(p.Y); arr.Add(p.Z); }
                return arr;
            }

            public object BuildNormalArrayOrNull()
            {
                if (_positions.Count == 0) return null;
                var arr = new List<double>(_accNormals.Count * 3);
                foreach (var n in _accNormals)
                {
                    XYZ nn = n;
                    try { nn = n.Normalize(); } catch { }
                    arr.Add(nn.X); arr.Add(nn.Y); arr.Add(nn.Z);
                }
                return arr;
            }

            public object BuildUvArrayOrNull()
            {
                // Placeholder UVs (0,0) per vertex
                if (_uvs.Count == 0) return null;
                var arr = new List<double>(_uvs.Count * 2);
                foreach (var uv in _uvs) { arr.Add(uv.U); arr.Add(uv.V); }
                return arr;
            }

            public object BuildSubmeshIndexArrays()
            {
                var groups = new List<object>(_matToIndices.Count);
                foreach (var kv in _matToIndices)
                    groups.Add(new { materialId = kv.Key, indices = kv.Value });
                return groups;
            }

            public object BuildMaterialArray(Document doc)
            {
                var list = new List<object>();
                foreach (var mid in _materialIds)
                {
                    if (mid <= 0) { list.Add(new { materialId = -1, name = "" }); continue; }
                    try
                    {
                        var m = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(mid)) as Autodesk.Revit.DB.Material;
                        list.Add(new { materialId = mid, name = m?.Name ?? "" });
                    }
                    catch { list.Add(new { materialId = mid, name = "" }); }
                }
                return list;
            }

            private readonly struct VertexKey : IEquatable<VertexKey>
            {
                private readonly long X; private readonly long Y; private readonly long Z;
                public VertexKey(XYZ p, double tol)
                {
                    // Quantize by tolerance (feet)
                    X = (long)Math.Round(p.X / tol);
                    Y = (long)Math.Round(p.Y / tol);
                    Z = (long)Math.Round(p.Z / tol);
                }
                public bool Equals(VertexKey other) => X == other.X && Y == other.Y && Z == other.Z;
                public override bool Equals(object obj) => obj is VertexKey k && Equals(k);
                public override int GetHashCode() => (X, Y, Z).GetHashCode();
            }
        }
    }
}


