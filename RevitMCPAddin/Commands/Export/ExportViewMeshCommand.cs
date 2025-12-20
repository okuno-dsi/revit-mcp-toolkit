// ================================================================
// File: Commands/Export/ExportViewMeshCommand.cs
// Purpose: Export visible render meshes ("what you see") of a 3D view,
//          grouped per element & material, GLTF/OBJ-friendly.
// Method:  CustomExporter + IExportContext (Polymesh) capture
// JSON-RPC: method = "export_view_mesh"
// Params: {
//   viewId: int,                      // 3D view element id
//   elementIds?: int[],               // optional subset
//   detailLevel?: "Coarse"|"Medium"|"Fine" (default "Fine")
//   includeLinked?: bool (default true)
//   unitsOut?: "feet"|"mm" (default "feet")
//   weld?: bool (default true)
//   weldTolerance?: number (feet, default 1e-6)
// }
// Result: {
//   ok: true,
//   units: "feet"|"mm",
//   viewId: int,
//   elements: [
//     {
//       elementId: int,
//       uniqueId: string,
//       transform: number[4][4],
//       materialSlots: [{ materialKey:int, name:string, color:[r,g,b], transparency:number }],
//       vertices: number[][3],        // unified vertex buffer (world-space, unitsOut)
//       submeshes: [{ materialKey:int, indices:int[] }]
//     }, ...
//   ]
// }
// ================================================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

// ================================================================
// File: Commands/Export/ExportViewMeshCommand.cs
// Purpose: Export visible render meshes ("what you see") of a 3D view,
//          grouped per element & material, GLTF/OBJ-friendly.
// Method:  CustomExporter + IExportContext (Polymesh) capture
// JSON-RPC: method = "export_view_mesh"
// Params: {
//   viewId: int,                      // 3D view element id
//   elementIds?: int[],               // optional subset
//   detailLevel?: "Coarse"|"Medium"|"Fine" (default "Fine")
//   includeLinked?: bool (default true)
//   unitsOut?: "feet"|"mm" (default "feet")
//   weld?: bool (default true)
//   weldTolerance?: number (feet, default 1e-6)
// }
// Result: {
//   ok: true,
//   units: "feet"|"mm",
//   viewId: int,
//   elements: [
//     {
//       elementId: int,
//       uniqueId: string,
//       transform: number[4][4],
//       materialSlots: [{ materialKey:int, name:string, color:[r,g,b], transparency:number }],
//       vertices: number[][3],        // unified vertex buffer (world-space, unitsOut)
//       submeshes: [{ materialKey:int, indices:int[] }]
//     }, ...
//   ]
// }
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Export
{
    public class ExportViewMeshCommand : IRevitCommandHandler
    {
        public string CommandName => "export_view_mesh";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "Active document not found." };

            var p = cmd.Params as JObject ?? new JObject();
            var viewIdInt = p.Value<int?>("viewId") ?? 0;
            if (viewIdInt <= 0) return new { ok = false, msg = "viewId is required." };
            var view = doc.GetElement(new ElementId(viewIdInt)) as View3D;
            if (view == null || view.ViewType != ViewType.ThreeD || view.IsTemplate)
                return new { ok = false, msg = "View not found or not a 3D view.", code = "INVALID_VIEW" };

            var elementIdsOpt = p["elementIds"] is JArray ja && ja.Count > 0
                ? new HashSet<int>(ja.Values<int>())
                : null;

            var detail = ParseDetailLevel(p.Value<string>("detailLevel"));
            bool includeLinked = p.Value<bool?>("includeLinked") ?? true;
            string unitsOut = (p.Value<string>("unitsOut") ?? "feet").Trim().ToLowerInvariant();
            bool weld = p.Value<bool?>("weld") ?? true;
            double weldTol = p.Value<double?>("weldTolerance") ?? 1e-6; // feet

            try
            {
                // Build exporter
                var opts = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = detail,
                    View = view
                };

                var ctx = new MeshExportContext(doc, elementIdsOpt, includeLinked, weld, weldTol, unitsOut);
                var exporter = new CustomExporter(doc, ctx)
                {
                    IncludeGeometricObjects = true,
                    ShouldStopOnError = false
                };

                // Export this view only
                exporter.Export(view);

                var elements = ctx.BuildResultElements();

                if (elements.Count == 0)
                    return new { ok = false, msg = "No visible geometry in the view.", code = "NO_VISIBLE_GEOMETRY" };

                return new
                {
                    ok = true,
                    units = unitsOut == "mm" ? "mm" : "feet",
                    viewId = viewIdInt,
                    elements
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("[export_view_mesh] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }

        private static ViewDetailLevel ParseDetailLevel(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ViewDetailLevel.Fine;
            s = s.Trim().ToLowerInvariant();
            if (s.StartsWith("c")) return ViewDetailLevel.Coarse;
            if (s.StartsWith("m")) return ViewDetailLevel.Medium;
            return ViewDetailLevel.Fine;
        }

        // ----------------- Export Context -----------------
        private class MeshExportContext : IExportContext
        {
            private readonly Document _doc;
            private readonly HashSet<int> _filterIds; // null => no filter
            private readonly bool _includeLinked;
            private readonly bool _weld;
            private readonly double _tol;
            private readonly string _unitsOut;

            private readonly Stack<Transform> _tStack = new Stack<Transform>();
            private readonly Stack<ElementId> _ownerStack = new Stack<ElementId>();
            private readonly Stack<ElementId> _matStack = new Stack<ElementId>();

            private readonly Dictionary<int, Collector> _collectors = new Dictionary<int, Collector>();

            public MeshExportContext(Document doc, HashSet<int> filterIds, bool includeLinked, bool weld, double weldTol, string unitsOut)
            {
                _doc = doc;
                _filterIds = filterIds;
                _includeLinked = includeLinked;
                _weld = weld;
                _tol = Math.Max(1e-9, weldTol);
                _unitsOut = (unitsOut ?? "feet").Trim().ToLowerInvariant();
            }

            public IList<object> BuildResultElements()
            {
                var res = new List<object>();
                foreach (var kv in _collectors)
                {
                    var eid = kv.Key;
                    var coll = kv.Value;
                    var elem = coll.Element ?? _doc.GetElement(new ElementId(eid));
                    if (elem == null) continue;

                    var mats = coll.BuildMaterialArray(_doc);
                    res.Add(new
                    {
                        elementId = eid,
                        uniqueId = elem.UniqueId,
                        transform = ToArray4x4(coll.RootTransform),
                        materialSlots = mats,
                        vertices = coll.BuildVertexArray(_unitsOut),
                        submeshes = coll.BuildSubmeshIndexArrays()
                    });
                }
                return res;
            }

            // ===== IExportContext 正しいシグネチャ =====
            public bool Start() { _tStack.Clear(); _ownerStack.Clear(); _matStack.Clear(); _tStack.Push(Transform.Identity); return true; }
            public void Finish() { }
            public void OnCancel() { }
            public bool IsCanceled() => false;

            public RenderNodeAction OnViewBegin(ViewNode node) => RenderNodeAction.Proceed;
            public void OnViewEnd(ElementId elementId) { }

            public RenderNodeAction OnElementBegin(ElementId elementId)
            {
                if (_filterIds != null && !_filterIds.Contains(elementId.IntegerValue))
                {
                    _ownerStack.Push(ElementId.InvalidElementId); // filter marker
                    return RenderNodeAction.Proceed;
                }
                _ownerStack.Push(elementId);
                return RenderNodeAction.Proceed;
            }
            public void OnElementEnd(ElementId elementId) { if (_ownerStack.Count > 0) _ownerStack.Pop(); }

            public RenderNodeAction OnInstanceBegin(InstanceNode node)
            {
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return RenderNodeAction.Proceed;
            }
            public void OnInstanceEnd(InstanceNode node) { if (_tStack.Count > 1) _tStack.Pop(); }

            public RenderNodeAction OnLinkBegin(LinkNode node)
            {
                if (!_includeLinked) return RenderNodeAction.Skip;
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return RenderNodeAction.Proceed;
            }
            public void OnLinkEnd(LinkNode node) { if (_tStack.Count > 1) _tStack.Pop(); }

            public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
            public void OnFaceEnd(FaceNode node) { }

            // ← ここは void 戻り値（2023/2024）
            public void OnMaterial(MaterialNode node) { _matStack.Push(node.MaterialId); }
            public void OnMaterialEnd(MaterialNode node) { if (_matStack.Count > 0) _matStack.Pop(); }

            // 必須だが中身はダミーでOK
            public void OnLight(LightNode node) { }
            public void OnRPC(RPCNode node) { }

            // ← ここも void 戻り値
            public void OnPolymesh(PolymeshTopology pm)
            {
                if (_ownerStack.Count > 0 && _ownerStack.Peek() == ElementId.InvalidElementId) return; // filtered

                int elementId = _ownerStack.Count > 0 ? _ownerStack.Peek().IntegerValue : -1;
                if (elementId <= 0) return;

                var t = _tStack.Peek();
                var matId = _matStack.Count > 0 ? _matStack.Peek() : ElementId.InvalidElementId;

                if (!_collectors.TryGetValue(elementId, out var coll))
                {
                    coll = new Collector(_weld, _tol) { Element = _doc.GetElement(new ElementId(elementId)), RootTransform = t };
                    _collectors[elementId] = coll;
                }

                var pts = pm.GetPoints();      // IList<XYZ>
                var facets = pm.GetFacets();   // IList<PolymeshFacet> (V1,V2,V3)

                var world = new XYZ[pts.Count];
                for (int i = 0; i < pts.Count; i++) world[i] = t.OfPoint(pts[i]);

                foreach (var f in facets)
                {
                    if (f.V1 < 0 || f.V2 < 0 || f.V3 < 0) continue;
                    coll.AddTriangle(matId, world[f.V1], world[f.V2], world[f.V3]);
                }
            }

            public bool AllowExportElement(ElementId id) => true;

            private static double[][] ToArray4x4(Transform t) => new[]
            {
                new[] { t.BasisX.X, t.BasisX.Y, t.BasisX.Z, t.Origin.X },
                new[] { t.BasisY.X, t.BasisY.Y, t.BasisY.Z, t.Origin.Y },
                new[] { t.BasisZ.X, t.BasisZ.Y, t.BasisZ.Z, t.Origin.Z },
                new[] { 0.0, 0.0, 0.0, 1.0 }
            };

            // ===== per-element collector（privateでOK）=====
            private sealed class Collector
            {
                private readonly bool _weld;
                private readonly double _tol;
                private readonly Dictionary<VertexKey, int> _lookup = new Dictionary<VertexKey, int>();
                private readonly List<XYZ> _pos = new List<XYZ>(4096);
                private readonly List<XYZ> _accN = new List<XYZ>(4096);
                private readonly Dictionary<int, List<int>> _matToIdx = new Dictionary<int, List<int>>();
                private readonly HashSet<int> _matSet = new HashSet<int>();
                public Element Element { get; set; }
                public Transform RootTransform { get; set; } = Transform.Identity;

                public Collector(bool weld, double tol) { _weld = weld; _tol = Math.Max(1e-9, tol); }

                public void AddTriangle(ElementId mat, XYZ p0, XYZ p1, XYZ p2)
                {
                    int key = (mat != null && mat.IntegerValue > 0) ? mat.IntegerValue : -1;
                    if (!_matToIdx.TryGetValue(key, out var list)) { list = new List<int>(2048); _matToIdx[key] = list; }
                    _matSet.Add(key);

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
                        var k = new VertexKey(p, _tol);
                        if (_lookup.TryGetValue(k, out int idx))
                        {
                            var acc = _accN[idx];
                            _accN[idx] = new XYZ(acc.X + n.X, acc.Y + n.Y, acc.Z + n.Z);
                            return idx;
                        }
                        int nid = CreateVertex(p, n);
                        _lookup[k] = nid;
                        return nid;
                    }
                    return CreateVertex(p, n);
                }

                private int CreateVertex(XYZ p, XYZ n)
                {
                    int idx = _pos.Count;
                    _pos.Add(p);
                    _accN.Add(n);
                    return idx;
                }

                private static XYZ FaceNormal(XYZ a, XYZ b, XYZ c)
                {
                    var u = b - a; var v = c - a;
                    var cr = u.CrossProduct(v);
                    double len = cr.GetLength();
                    if (len < 1e-12) return new XYZ(0, 0, 1);
                    return new XYZ(cr.X / len, cr.Y / len, cr.Z / len);
                }

                public double[][] BuildVertexArray(string unitsOut)
                {
                    double scale = (unitsOut == "mm") ? 304.8 : 1.0;
                    var arr = new double[_pos.Count][];
                    for (int i = 0; i < _pos.Count; i++)
                    {
                        var p = _pos[i];
                        arr[i] = new[] { p.X * scale, p.Y * scale, p.Z * scale };
                    }
                    return arr;
                }

                public object[] BuildSubmeshIndexArrays()
                {
                    var list = new List<object>();
                    foreach (var kv in _matToIdx) list.Add(new { materialKey = kv.Key, indices = kv.Value.ToArray() });
                    return list.ToArray();
                }

                public object[] BuildMaterialArray(Document doc)
                {
                    var list = new List<object>();
                    foreach (var mid in _matSet)
                    {
                        if (mid <= 0) { list.Add(new { materialKey = -1, name = "Unknown", color = new[] { 0, 0, 0 }, transparency = 0.0 }); continue; }
                        var m = doc.GetElement(new ElementId(mid)) as Autodesk.Revit.DB.Material;
                        if (m == null) list.Add(new { materialKey = mid, name = "Unknown(" + mid + ")", color = new[] { 0, 0, 0 }, transparency = 0.0 });
                        else
                        {
                            var c = m.Color;
                            list.Add(new { materialKey = mid, name = m.Name, color = new[] { (int)c.Red, (int)c.Green, (int)c.Blue }, transparency = m.Transparency / 100.0 });
                        }
                    }
                    return list.ToArray();
                }

                // ← ここを internal にしてもOK（環境により可視性警告が出る場合）
                private struct VertexKey : IEquatable<VertexKey>
                {
                    private readonly long _x, _y, _z;
                    public VertexKey(XYZ p, double tol)
                    {
                        double inv = 1.0 / tol;
                        _x = (long)Math.Round(p.X * inv);
                        _y = (long)Math.Round(p.Y * inv);
                        _z = (long)Math.Round(p.Z * inv);
                    }
                    public bool Equals(VertexKey other) => _x == other._x && _y == other._y && _z == other._z;
                    public override bool Equals(object obj) => obj is VertexKey k && Equals(k);
                    public override int GetHashCode() { unchecked { int h = 17; h = h * 31 + _x.GetHashCode(); h = h * 31 + _y.GetHashCode(); h = h * 31 + _z.GetHashCode(); return h; } }
                }
            }
        }
    }
}