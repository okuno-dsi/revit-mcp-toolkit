// ================================================================
// File: Commands/ElementOps/GetInstanceGeometryCommand.cs
// Purpose:
//   Return polygon mesh (GLTF/OBJ friendly) of a single element.
//   - Supports elementId or uniqueId
//   - World-space vertices (Revit internal units = feet)
//   - Triangulated from Solids (Face.Triangulate) and Mesh geometry
//   - Properly multiplies nested GeometryInstance / Family transforms
//   - Vertex welding with tolerance (reduces duplicates)
//   - Submeshes per material (if resolvable)
//   - Returns normals/uvs when available (best-effort; may be null)
// Author: O-chan companion 🛠
// Target: .NET Framework 4.8 / Revit 2023/2024
// ================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps
{
    public class GetInstanceGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_instance_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "Active document not found." };

            var p = cmd.Params as JObject ?? new JObject();

            // Resolve target element: either elementId (int) or uniqueId (string)
            Element elem = null;
            if (p.TryGetValue("elementId", out var jId) && int.TryParse(jId.ToString(), out var eid))
            {
                elem = doc.GetElement(new ElementId(eid));
            }
            else if (p.TryGetValue("uniqueId", out var jUid))
            {
                elem = doc.GetElement(jUid.ToString());
            }

            if (elem == null)
                return new { ok = false, msg = "Element not found. Provide elementId or uniqueId." };

            // Options
            var detail = ParseDetailLevel(p.Value<string>("detailLevel")); // "Coarse"|"Medium"|"Fine"
            bool includeNonVisible = p.Value<bool?>("includeNonVisible") ?? false;
            bool weld = p.Value<bool?>("weld") ?? true;
            double weldTol = p.Value<double?>("weldTolerance") ?? 1e-6; // feet

            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = includeNonVisible,
                DetailLevel = detail
            };

            var ge = elem.get_Geometry(opts);
            if (ge == null)
                return new { ok = false, msg = "Geometry is null (element may not have visible geometry in this view or options)." };

            // Collector for GLTF/OBJ-friendly output
            var collector = new MeshCollector(weld, weldTol);

            // Element root transform (e.g., FamilyInstance)
            var rootT = GetElementRootTransform(elem);

            // Traverse geometry with proper transform accumulation
            try
            {
                TraverseGeometry(ge, rootT, doc, collector);
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Geometry traversal failed: " + ex.Message };
            }

            if (collector.TotalTriangleCount == 0 && collector.TotalVertexCount == 0)
            {
                return new { ok = false, msg = "No triangulatable geometry found." };
            }

            // Build response object
            var matArray = collector.BuildMaterialArray(doc);
            var response = new
            {
                ok = true,
                elementId = elem.Id.IntegerValue,
                uniqueId = elem.UniqueId,
                category = elem.Category?.Name,
                typeName = elem.Document.GetElement(elem.GetTypeId())?.Name,
                units = "feet",
                transform = ToArray4x4(rootT),
                // unified vertex buffer (positions)
                vertices = collector.BuildVertexArray(),
                // optional buffers
                normals = collector.BuildNormalArrayOrNull(),
                uvs = collector.BuildUvArrayOrNull(),
                // submesh groups (per material)
                submeshes = collector.BuildSubmeshIndexArrays(),
                materials = matArray,
            };

            return response;
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
            // Most elements: Identity. FamilyInstance: instance transform.
            if (e is FamilyInstance fi)
            {
                try
                {
                    var t = fi.GetTransform();
                    if (t != null) return t;
                }
                catch { /* some categories may throw; ignore */ }
            }
            return Transform.Identity;
        }

        private static void TraverseGeometry(GeometryElement ge, Transform current, Document doc, MeshCollector collector)
        {
            foreach (var obj in ge)
            {
                if (obj is GeometryInstance gi)
                {
                    var instT = current.Multiply(gi.Transform);
                    var instanceGe = gi.GetInstanceGeometry();
                    if (instanceGe != null)
                        TraverseGeometry(instanceGe, instT, doc, collector);
                }
                else if (obj is Solid solid && solid.Volume > 0)
                {
                    AddSolid(solid, current, doc, collector);
                }
                else if (obj is Mesh mesh)
                {
                    AddMesh(mesh, current, /*matId*/ElementId.InvalidElementId, collector);
                }
                // Curves, Points, etc. are ignored for mesh export
            }
        }

        private static void AddSolid(Autodesk.Revit.DB.Solid solid, Autodesk.Revit.DB.Transform t, Autodesk.Revit.DB.Document doc, MeshCollector collector)
        {
            if (solid == null || solid.Faces == null || solid.Faces.Size == 0) return;

            foreach (Autodesk.Revit.DB.Face f in solid.Faces)
            {
                Autodesk.Revit.DB.Mesh m = null;
                try
                {
                    // Face は namespace と衝突しがちなので完全修飾
                    m = f.Triangulate();
                }
                catch
                {
                    // Triangulate に失敗する面もあるので安全にスキップ
                    continue;
                }

                if (m == null || m.NumTriangles == 0) continue;

                var matId =
                    (f.MaterialElementId != null && f.MaterialElementId.IntegerValue > 0)
                    ? f.MaterialElementId
                    : Autodesk.Revit.DB.ElementId.InvalidElementId;

                // ここで三角形ループは回さず、共通ヘルパーに渡す
                AddMesh(m, t, matId, collector);
            }
        }

        private static void AddMesh(Autodesk.Revit.DB.Mesh m, Autodesk.Revit.DB.Transform t, Autodesk.Revit.DB.ElementId matId, MeshCollector collector)
        {
            int vCount = m.Vertices.Count;
            var transformed = new Autodesk.Revit.DB.XYZ[vCount];

            for (int i = 0; i < vCount; i++)
            {
                transformed[i] = t.OfPoint(m.Vertices[i]);
            }

            for (int ti = 0; ti < m.NumTriangles; ti++)
            {
                var tri = m.get_Triangle(ti);

                // Revit のバージョン差吸収：uint→int 明示キャスト
                int a = Convert.ToInt32(tri.get_Index(0));
                int b = Convert.ToInt32(tri.get_Index(1));
                int c = Convert.ToInt32(tri.get_Index(2));

                collector.AddTriangle(
                    matId,
                    transformed[a], transformed[b], transformed[c]
                );
            }
        }

        private static double[][] ToArray4x4(Transform t)
        {
            // Revit Transform is 3x4; export as 4x4 row-major for GLTF/OBJ helper usage
            var m = new double[4][]
            {
                new double[]{ t.BasisX.X, t.BasisX.Y, t.BasisX.Z, t.Origin.X },
                new double[]{ t.BasisY.X, t.BasisY.Y, t.BasisY.Z, t.Origin.Y },
                new double[]{ t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, t.Origin.Z },
                new double[]{ 0, 0, 0, 1 }
            };
            return m;
        }

        // ============================
        // Internal collector classes
        // ============================
        private class MeshCollector
        {
            private readonly bool _weld;
            private readonly double _tol;
            private readonly Dictionary<VertexKey, int> _vertexLookup;
            private readonly List<XYZ> _positions;
            private readonly List<XYZ> _accNormals; // accumulate to average
            private readonly List<UV> _uvs;         // currently not filled; placeholder for future
            // Material-grouped index lists
            private readonly Dictionary<int, List<int>> _matToIndices; // flat index buffer per material key
            private readonly HashSet<int> _materialIds; // Revit material int ids encountered

            public MeshCollector(bool weld, double weldTolerance)
            {
                _weld = weld;
                _tol = Math.Max(1e-9, weldTolerance);
                _vertexLookup = new Dictionary<VertexKey, int>();
                _positions = new List<XYZ>(8192);
                _accNormals = new List<XYZ>(8192);
                _uvs = new List<UV>(8192);
                _matToIndices = new Dictionary<int, List<int>>();
                _materialIds = new HashSet<int>();
            }

            public int TotalVertexCount => _positions.Count;
            public int TotalTriangleCount
            {
                get
                {
                    int tris = 0;
                    foreach (var kv in _matToIndices)
                        tris += kv.Value.Count / 3;
                    return tris;
                }
            }

            public void AddTriangle(ElementId matId, XYZ p0, XYZ p1, XYZ p2)
            {
                int matKey = (matId != null && matId.IntegerValue > 0) ? matId.IntegerValue : -1;
                if (!_matToIndices.TryGetValue(matKey, out var list))
                {
                    list = new List<int>(4096);
                    _matToIndices[matKey] = list;
                }
                _materialIds.Add(matKey);

                // Compute face normal (simple)
                var n = FaceNormal(p0, p1, p2);

                int i0 = AddVertex(p0, n);
                int i1 = AddVertex(p1, n);
                int i2 = AddVertex(p2, n);

                list.Add(i0);
                list.Add(i1);
                list.Add(i2);
            }

            private int AddVertex(XYZ p, XYZ n)
            {
                int index;
                if (_weld)
                {
                    var k = new VertexKey(p, _tol);
                    if (_vertexLookup.TryGetValue(k, out index))
                    {
                        // accumulate normal for smoothing
                        var acc = _accNormals[index];
                        _accNormals[index] = new XYZ(acc.X + n.X, acc.Y + n.Y, acc.Z + n.Z);
                        return index;
                    }
                    index = CreateVertex(p, n);
                    _vertexLookup[k] = index;
                    return index;
                }
                else
                {
                    index = CreateVertex(p, n);
                    return index;
                }
            }

            private int CreateVertex(XYZ p, XYZ n)
            {
                int idx = _positions.Count;
                _positions.Add(p);
                _accNormals.Add(n);
                _uvs.Add(new UV(0, 0)); // placeholder (no UV)
                return idx;
            }

            private static XYZ FaceNormal(XYZ a, XYZ b, XYZ c)
            {
                var u = b - a;
                var v = c - a;
                var cross = u.CrossProduct(v);
                double len = cross.GetLength();
                if (len < 1e-12) return new XYZ(0, 0, 1);
                return new XYZ(cross.X / len, cross.Y / len, cross.Z / len);
            }

            public double[][] BuildVertexArray()
            {
                var arr = new double[_positions.Count][];
                for (int i = 0; i < _positions.Count; i++)
                {
                    var p = _positions[i];
                    arr[i] = new[] { p.X, p.Y, p.Z };
                }
                return arr;
            }

            public double[][] BuildNormalArrayOrNull()
            {
                if (_positions.Count == 0) return null;
                var arr = new double[_accNormals.Count][];
                for (int i = 0; i < _accNormals.Count; i++)
                {
                    var n = _accNormals[i];
                    double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                    if (len < 1e-12) { arr[i] = new[] { 0.0, 0.0, 1.0 }; continue; }
                    arr[i] = new[] { n.X / len, n.Y / len, n.Z / len };
                }
                return arr;
            }

            public double[][] BuildUvArrayOrNull()
            {
                // We currently do not compute UVs from Revit Mesh reliably for all cases.
                // Return null to keep schema simple for GLTF/OBJ pipelines.
                return null;
            }

            public object[] BuildSubmeshIndexArrays()
            {
                // GLTF/OBJ-friendly: groups of triangle indices per material
                // Each submesh: { materialKey, indices: [i0,i1,i2,...] }
                var list = new List<object>();
                foreach (var kv in _matToIndices)
                {
                    list.Add(new
                    {
                        materialKey = kv.Key, // -1 if unknown
                        indices = kv.Value.ToArray()
                    });
                }
                return list.ToArray();
            }

            public object[] BuildMaterialArray(Autodesk.Revit.DB.Document doc)
            {
                var list = new List<object>();
                foreach (var mid in _materialIds)
                {
                    if (mid <= 0)
                    {
                        list.Add(new
                        {
                            materialKey = -1,
                            name = "Unknown",
                            color = new[] { 0, 0, 0 },
                            transparency = 0.0
                        });
                        continue;
                    }

                    var m = doc.GetElement(new Autodesk.Revit.DB.ElementId(mid)) as Autodesk.Revit.DB.Material;
                    if (m == null)
                    {
                        list.Add(new
                        {
                            materialKey = mid,
                            name = "Unknown(" + mid + ")",
                            color = new[] { 0, 0, 0 },
                            transparency = 0.0
                        });
                    }
                    else
                    {
                        var c = m.Color;
                        double tr = (m.Transparency / 100.0);
                        list.Add(new
                        {
                            materialKey = mid,
                            name = m.Name,
                            color = new[] { (int)c.Red, (int)c.Green, (int)c.Blue },
                            transparency = tr
                        });
                    }
                }
                return list.ToArray();
            }

            // Vertex key with tolerance-based hashing (for welding)
            private struct VertexKey : IEquatable<VertexKey>
            {
                private readonly long _x, _y, _z;
                private const double Scale = 1e9; // for robust rounding

                public VertexKey(XYZ p, double tol)
                {
                    // Round to tolerance grid
                    double inv = 1.0 / tol;
                    _x = (long)Math.Round(p.X * inv);
                    _y = (long)Math.Round(p.Y * inv);
                    _z = (long)Math.Round(p.Z * inv);
                }

                public bool Equals(VertexKey other)
                {
                    return _x == other._x && _y == other._y && _z == other._z;
                }

                public override bool Equals(object obj)
                {
                    return obj is VertexKey k && Equals(k);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int h = 17;
                        h = h * 31 + _x.GetHashCode();
                        h = h * 31 + _y.GetHashCode();
                        h = h * 31 + _z.GetHashCode();
                        return h;
                    }
                }
            }
        }
    }
}
