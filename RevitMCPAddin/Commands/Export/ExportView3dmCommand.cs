// ============================================================================
// File: RevitMCPAddin/Commands/Export/ExportView3dmCommand.cs
// Desc: Visible geometry in a 3D view -> .3dm (Rhino) as meshes
// JSON-RPC: method = "export_view_3dm"
// Params: {
//   viewId: int,                      // required (3D view id)
//   outPath: string,                  // required (e.g. "C:/Exports/Model.3dm")
//   elementIds?: int[],               // optional subset filter
//   detailLevel?: "Coarse"|"Medium"|"Fine" (currently not enforced by CustomExporter)
//   includeLinked?: bool (default true)
//   unitsOut?: "mm"|"feet" (default "mm")
//   weld?: bool (default true)
//   weldTolerance?: number (feet; default 1e-6)
//   layerMode?: "byCategory"|"byType"|"byElement"|"single" (default "byCategory")
// }
// Result: { ok:true, path:string, viewId:int, counts:{elements:int, meshes:int, vertices:int, faces:int}, units:"mm"|"feet" }
// ----------------------------------------------------------------------------
// Notes:
// - Requires NuGet: Rhino3dm (Rhino.FileIO / Rhino.Geometry for .3dm write)
// - Keep all Revit types qualified via alias 'Rvt', Rhino via 'Rg' and 'Rfi'.
// - Geometry is exported as triangulated meshes (OnPolymesh).
// - Per-object attributes store Revit metadata (ElementId/UniqueId/Category/TypeName).
// ============================================================================

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Rfi = Rhino.FileIO;
using Rg = Rhino.Geometry;
using Rvt = Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands.Export
{
    public sealed class ExportView3dmCommand : IRevitCommandHandler
    {
        public string CommandName => "export_view_3dm";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "Active document not found." };

            var p = cmd.Params as JObject ?? new JObject();

            int viewIdInt = p.Value<int?>("viewId") ?? 0;
            string outPath = (p.Value<string>("outPath") ?? "").Trim();

            if (viewIdInt <= 0) return new { ok = false, msg = "viewId is required.", code = "ARG_VIEWID" };
            if (string.IsNullOrWhiteSpace(outPath)) return new { ok = false, msg = "outPath is required.", code = "ARG_OUTPATH" };

            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as Rvt.View3D;
            if (view == null || view.ViewType != Rvt.ViewType.ThreeD || view.IsTemplate)
                return new { ok = false, msg = "View not found or not a 3D view.", code = "INVALID_VIEW" };

            // Optional filters / options
            HashSet<int> elementIdsOpt = null;
            if (p["elementIds"] is JArray ja && ja.Count > 0)
                elementIdsOpt = new HashSet<int>(ja.Values<int>());

            // We parse detailLevel for API symmetry; CustomExporter uses the view's current display/mesh; we don't set it here.
            string detailLevel = (p.Value<string>("detailLevel") ?? "Fine").Trim();

            bool includeLinked = p.Value<bool?>("includeLinked") ?? true;
            string unitsOut = (p.Value<string>("unitsOut") ?? "mm").Trim().ToLowerInvariant(); // "mm" | "feet"
            bool weld = p.Value<bool?>("weld") ?? true;
            double weldTolFeet = p.Value<double?>("weldTolerance") ?? 1e-6; // feet domain
            string layerMode = (p.Value<string>("layerMode") ?? "byCategory").Trim().ToLowerInvariant();
            // Resolve output length unit from UnitHelper (Project settings) unless explicitly provided
            var mode = UnitHelper.ResolveUnitsMode(doc, p);
            Autodesk.Revit.DB.ForgeTypeId lenUnit;
            if (!string.IsNullOrWhiteSpace(unitsOut))
            {
                lenUnit = (unitsOut == "mm") ? Autodesk.Revit.DB.UnitTypeId.Millimeters : Autodesk.Revit.DB.UnitTypeId.Feet;
            }
            else if (mode == UnitsMode.Project)
            {
                lenUnit = doc.GetUnits().GetFormatOptions(Autodesk.Revit.DB.SpecTypeId.Length).GetUnitTypeId();
            }
            else if (mode == UnitsMode.Raw)
            {
                lenUnit = Autodesk.Revit.DB.UnitTypeId.Feet;
            }
            else // UnitsMode.SI or fallback
            {
                lenUnit = Autodesk.Revit.DB.UnitTypeId.Millimeters;
            }
            double __scale = Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits(1.0, lenUnit);

            try
            {
                // Ensure folder and overwrite quietly
                var folder = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                if (File.Exists(outPath)) File.Delete(outPath);

                // Unit scaling: Revit is feet. If unitsOut == "mm", scale by 304.8.
                double scale = (unitsOut == "mm") ? 304.8 : 1.0;
                // Weld tolerance converted to output unit domain
                double tolScaled = (unitsOut == "mm") ? (weldTolFeet * 304.8) : weldTolFeet;

                // Collect polymeshes via CustomExporter
                var ctx = new MeshExportContext(doc, elementIdsOpt, includeLinked, weld, tolScaled, unitsOut, layerMode, scale);

                var exporter = new Rvt.CustomExporter(doc, ctx)
                {
                    IncludeGeometricObjects = true,
                    ShouldStopOnError = false
                };

                exporter.Export(view); // uses the current view's visibility/mesh tessellation

                // Write .3dm
                var file = new Rfi.File3dm();
                file.Settings.ModelUnitSystem = (lenUnit == Autodesk.Revit.DB.UnitTypeId.Millimeters) ? Rhino.UnitSystem.Millimeters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Meters) ? Rhino.UnitSystem.Meters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Centimeters) ? Rhino.UnitSystem.Centimeters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Inches) ? Rhino.UnitSystem.Inches : Rhino.UnitSystem.Feet;

                ctx.EmitTo3dm(file);
                // Write Rhino 8 format (change version if needed)
                file.Write(outPath, 8);

                var stat = ctx.GetStats();

                return new
                {
                    ok = true,
                    path = outPath,
                    units = (unitsOut == "mm") ? "mm" : "feet",
                    viewId = viewIdInt,
                    counts = stat
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("[export_view_3dm] " + ex);
                return new { ok = false, msg = ex.Message, code = "EXCEPTION" };
            }
        }

        // ====================================================================
        // MeshExportContext : IExportContext
        // Captures polymeshes per element/material; writes to .3dm as meshes
        // ====================================================================
        private sealed class MeshExportContext : Rvt.IExportContext
        {
            private readonly Rvt.Document _doc;
            private readonly HashSet<int> _filterIds; // null => export all
            private readonly bool _includeLinked;
            private readonly bool _weld;
            private readonly double _tol; // already scaled to output unit domain
            private readonly string _unitsOut; // "mm" or "feet"
            private readonly string _layerMode; // "byCategory"|"byType"|"byElement"|"single"
            private readonly double _scale; // feet->(mm|feet)

            private readonly Stack<Rvt.Transform> _tStack = new Stack<Rvt.Transform>();
            private readonly Stack<Rvt.ElementId> _ownerStack = new Stack<Rvt.ElementId>();
            private readonly Stack<Rvt.ElementId> _matStack = new Stack<Rvt.ElementId>();

            private readonly Dictionary<int, PerElement> _elements = new Dictionary<int, PerElement>();

            private object _stat = new { elements = 0, meshes = 0, vertices = 0, faces = 0 };

            public MeshExportContext(
                Rvt.Document doc,
                HashSet<int> filterIds,
                bool includeLinked,
                bool weld,
                double tolScaled,
                string unitsOut,
                string layerMode,
                double scale)
            {
                _doc = doc;
                _filterIds = filterIds;
                _includeLinked = includeLinked;
                _weld = weld;
                _tol = Math.Max(1e-12, tolScaled);
                _unitsOut = (unitsOut ?? "mm").Trim().ToLowerInvariant();
                _layerMode = string.IsNullOrWhiteSpace(layerMode) ? "byCategory" : layerMode.ToLowerInvariant();
                _scale = (scale > 0) ? scale : 1.0;
            }

            // ---- IExportContext ----
            public bool Start()
            {
                _tStack.Clear();
                _ownerStack.Clear();
                _matStack.Clear();
                _tStack.Push(Rvt.Transform.Identity);
                return true;
            }
            public void Finish() { }
            public bool IsCanceled() => false;
            public void OnCancel() { }

            public Rvt.RenderNodeAction OnViewBegin(Rvt.ViewNode node) => Rvt.RenderNodeAction.Proceed;
            public void OnViewEnd(Rvt.ElementId elementId) { }

            public Rvt.RenderNodeAction OnElementBegin(Rvt.ElementId elementId)
            {
                if (_filterIds != null && !_filterIds.Contains(elementId.IntValue()))
                {
                    _ownerStack.Push(Rvt.ElementId.InvalidElementId); // mark as filtered
                    return Rvt.RenderNodeAction.Proceed;
                }
                _ownerStack.Push(elementId);
                return Rvt.RenderNodeAction.Proceed;
            }
            public void OnElementEnd(Rvt.ElementId elementId)
            {
                if (_ownerStack.Count > 0) _ownerStack.Pop();
            }

            public Rvt.RenderNodeAction OnInstanceBegin(Rvt.InstanceNode node)
            {
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return Rvt.RenderNodeAction.Proceed;
            }
            public void OnInstanceEnd(Rvt.InstanceNode node)
            {
                if (_tStack.Count > 1) _tStack.Pop();
            }

            public Rvt.RenderNodeAction OnLinkBegin(Rvt.LinkNode node)
            {
                if (!_includeLinked) return Rvt.RenderNodeAction.Skip;
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return Rvt.RenderNodeAction.Proceed;
            }
            public void OnLinkEnd(Rvt.LinkNode node)
            {
                if (_tStack.Count > 1) _tStack.Pop();
            }

            public Rvt.RenderNodeAction OnFaceBegin(Rvt.FaceNode node) => Rvt.RenderNodeAction.Proceed;
            public void OnFaceEnd(Rvt.FaceNode node) { }

            public void OnMaterial(Rvt.MaterialNode node) { _matStack.Push(node.MaterialId); }
            public void OnMaterialEnd(Rvt.MaterialNode node) { if (_matStack.Count > 0) _matStack.Pop(); }

            public void OnLight(Rvt.LightNode node) { }
            public void OnRPC(Rvt.RPCNode node) { }

            public void OnPolymesh(Rvt.PolymeshTopology pm)
            {
                if (_ownerStack.Count > 0 && _ownerStack.Peek() == Rvt.ElementId.InvalidElementId) return;

                int eid = _ownerStack.Count > 0 ? _ownerStack.Peek().IntValue() : -1;
                if (eid <= 0) return;

                Rvt.Transform t = _tStack.Peek();
                var matId = _matStack.Count > 0 ? _matStack.Peek() : Rvt.ElementId.InvalidElementId;

                if (!_elements.TryGetValue(eid, out var pe))
                {
                    var e = _doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                    pe = new PerElement
                    {
                        ElementId = eid,
                        UniqueId = e?.UniqueId ?? "",
                        Category = e?.Category?.Name ?? "Unknown",
                        TypeName = TryTypeName(e),
                        RootTransform = t
                    };
                    _elements[eid] = pe;
                }

                var pts = pm.GetPoints();    // IList<Rvt.XYZ>
                var facets = pm.GetFacets();    // IList<Rvt.PolymeshFacet>

                // Transform to world & scale to output unit domain once
                var world = new Rvt.XYZ[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    var w = t.OfPoint(pts[i]);
                    world[i] = new Rvt.XYZ(w.X * _scale, w.Y * _scale, w.Z * _scale);
                }

                int matKey = (matId != null && matId.IntValue() > 0) ? matId.IntValue() : -1;
                if (!pe.Sets.TryGetValue(matKey, out var set))
                {
                    set = new WeldSet(_weld, _tol);
                    pe.Sets[matKey] = set;
                }

                for (int i = 0; i < facets.Count; i++)
                {
                    var f = facets[i];
                    if (f.V1 < 0 || f.V2 < 0 || f.V3 < 0) continue;
                    set.AddTriangle(world[f.V1], world[f.V2], world[f.V3]);
                }
            }

            public bool AllowExportElement(Rvt.ElementId id) => true;

            // ---- Emit to .3dm ----
            public void EmitTo3dm(Rfi.File3dm file)
            {
                var layerMap = new Dictionary<string, int>();

                int EnsureLayer(string name)
                {
                    if (layerMap.TryGetValue(name, out int idx)) return idx;
                    var layer = new Rhino.DocObjects.Layer { Name = name };
                    file.AllLayers.Add(layer);                // rhino3dm: Add returns void
                    int newIndex = file.AllLayers.Count - 1;  // last index
                    layerMap[name] = newIndex;
                    return newIndex;
                }

                int vertices = 0, faces = 0, meshes = 0;

                foreach (var kv in _elements)
                {
                    var pe = kv.Value;
                    foreach (var kv2 in pe.Sets) // matKey -> WeldSet
                    {
                        var mset = kv2.Value;

                        var data = mset.BuildMeshData();
                        var pos = data.pos; // List<Rg.Point3d>
                        var tri = data.tri; // List<int> (tri indices)

                        if (pos.Count < 3 || tri.Count < 3) continue;

                        var rmesh = new Rg.Mesh();
                        rmesh.Vertices.AddVertices(pos);

                        // triangle index triplets
                        for (int i = 0; i < tri.Count; i += 3)
                        {
                            int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                            if (a >= 0 && b >= 0 && c >= 0 &&
                                a < rmesh.Vertices.Count &&
                                b < rmesh.Vertices.Count &&
                                c < rmesh.Vertices.Count)
                            {
                                rmesh.Faces.AddFace(a, b, c);
                            }
                        }

                        rmesh.Normals.ComputeNormals();
                        rmesh.Compact();

                        string layerName = _layerMode switch
                        {
                            "byelement" => $"Element::{pe.ElementId}",
                            "bytype" => $"Type::{Safe(pe.TypeName)}",
                            "single" => "RevitExport",
                            _ => $"Category::{Safe(pe.Category)}", // default byCategory
                        };
                        int layerIndex = EnsureLayer(layerName);

                        var attr = new Rhino.DocObjects.ObjectAttributes
                        {
                            LayerIndex = layerIndex
                        };
                        // store Revit metadata
                        attr.SetUserString("Revit.ElementId", pe.ElementId.ToString());
                        if (!string.IsNullOrEmpty(pe.UniqueId)) attr.SetUserString("Revit.UniqueId", pe.UniqueId);
                        if (!string.IsNullOrEmpty(pe.Category)) attr.SetUserString("Revit.Category", pe.Category);
                        if (!string.IsNullOrEmpty(pe.TypeName)) attr.SetUserString("Revit.TypeName", pe.TypeName);

                        file.Objects.AddMesh(rmesh, attr);

                        vertices += rmesh.Vertices.Count;
                        faces += rmesh.Faces.Count;
                        meshes++;
                    }
                }

                _stat = new { elements = _elements.Count, meshes, vertices, faces };
            }

            public object GetStats() => _stat;

            private static string TryTypeName(Rvt.Element e)
            {
                try
                {
                    var tid = e?.GetTypeId();
                    if (tid != null && tid.IntValue() > 0)
                    {
                        var t = e.Document.GetElement(tid) as Rvt.ElementType;
                        return t?.Name ?? "";
                    }
                }
                catch { }
                return "";
            }

            private static string Safe(string s)
                => string.IsNullOrWhiteSpace(s) ? "Unknown" : s.Replace("::", "_");

            // ---- Aggregation containers ----
            private sealed class PerElement
            {
                public int ElementId;
                public string UniqueId;
                public string Category;
                public string TypeName;
                public Rvt.Transform RootTransform = Rvt.Transform.Identity;
                public Dictionary<int, WeldSet> Sets = new Dictionary<int, WeldSet>(); // matKey -> welded triangles
            }

            private sealed class WeldSet
            {
                private readonly bool _weld;
                private readonly double _tol; // in output unit domain
                private readonly Dictionary<VKey, int> _map = new Dictionary<VKey, int>();
                private readonly List<Rg.Point3d> _pos = new List<Rg.Point3d>(2048);
                private readonly List<int> _tri = new List<int>(4096);

                public WeldSet(bool weld, double tol) { _weld = weld; _tol = Math.Max(1e-12, tol); }

                public void AddTriangle(Rvt.XYZ a, Rvt.XYZ b, Rvt.XYZ c)
                {
                    int i0 = Add(a); int i1 = Add(b); int i2 = Add(c);
                    _tri.Add(i0); _tri.Add(i1); _tri.Add(i2);
                }

                private int Add(Rvt.XYZ p)
                {
                    if (_weld)
                    {
                        var k = new VKey(p, _tol);
                        if (_map.TryGetValue(k, out int idx)) return idx;
                        int nid = Create(p);
                        _map[k] = nid;
                        return nid;
                    }
                    return Create(p);
                }

                private int Create(Rvt.XYZ p)
                {
                    int idx = _pos.Count;
                    _pos.Add(new Rg.Point3d(p.X, p.Y, p.Z));
                    return idx;
                }

                public (List<Rg.Point3d> pos, List<int> tri) BuildMeshData() => (_pos, _tri);

                private readonly struct VKey : IEquatable<VKey>
                {
                    private readonly long X, Y, Z;
                    public VKey(Rvt.XYZ p, double tol)
                    {
                        double inv = (tol <= 0) ? 1e9 : 1.0 / tol;
                        X = (long)Math.Round(p.X * inv);
                        Y = (long)Math.Round(p.Y * inv);
                        Z = (long)Math.Round(p.Z * inv);
                    }
                    public bool Equals(VKey o) => X == o.X && Y == o.Y && Z == o.Z;
                    public override bool Equals(object obj) => obj is VKey v && Equals(v);
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
            }
        }
    }
}



