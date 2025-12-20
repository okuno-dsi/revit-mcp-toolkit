// ============================================================================
// File: RevitMCPAddin/Commands/Export/ExportView3dmBrepCommand.cs
// Desc: Visible geometry in a 3D view -> .3dm (Rhino) as Breps where possible
//       - Planar faces -> Brep via Mesh (Solid/Face.Triangulate -> Brep.CreateFromMesh(mesh, true))
//       - Non-planar faces -> safe fallback to Mesh
// JSON-RPC: method = "export_view_3dm_brep"
// Params: {
//   viewId: int,                      // required (3D view id)
//   outPath: string,                  // required (e.g. "C:/Exports/Model.3dm")
//   elementIds?: int[],               // optional subset filter
//   includeLinked?: bool (default true)
//   unitsOut?: "mm"|"feet" (default "mm")
//   layerMode?: "byCategory"|"byType"|"byElement"|"single" (default "byCategory")
//   curveTol?: number (feet; default 1e-4)     // curve approx tolerance (feet domain)
//   angTolDeg?: number (default 1.0)           // angular tolerance degrees for polyline approx
// }
// Result: { ok:true, path:string, viewId:int,
//           counts:{elements:int, breps:int, meshes:int, curves:int, vertices:int, faces:int},
//           units:"mm"|"feet" }
// ----------------------------------------------------------------------------
// Requirements:
//   - .NET Framework 4.8
//   - NuGet: Rhino3dm
// Notes:
//   - Revit types qualified as 'Rvt', Rhino as 'Rg' and 'Rfi' to avoid ambiguity.
//   - Planar faces are converted to Brep via mesh route; others are meshed.
//   - ArcCurve construction fixed (3-point Arc -> ArcCurve).
//   - Triangle indices cast from uint to int.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using Rvt = Autodesk.Revit.DB;
using Rg = Rhino.Geometry;
using Rfi = Rhino.FileIO;

namespace RevitMCPAddin.Commands.Export
{
    public sealed class ExportView3dmBrepCommand : IRevitCommandHandler
    {
        public string CommandName => "export_view_3dm_brep";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "Active document not found." };

            var p = cmd.Params as JObject ?? new JObject();

            int viewIdInt = p.Value<int?>("viewId") ?? 0;
            string outPath = (p.Value<string>("outPath") ?? "").Trim();
            if (viewIdInt <= 0) return new { ok = false, msg = "viewId is required.", code = "ARG_VIEWID" };
            if (string.IsNullOrWhiteSpace(outPath))
                return new { ok = false, msg = "outPath is required.", code = "ARG_OUTPATH" };

            var view = doc.GetElement(new Rvt.ElementId(viewIdInt)) as Rvt.View3D;
            if (view == null || view.ViewType != Rvt.ViewType.ThreeD || view.IsTemplate)
                return new { ok = false, msg = "View not found or not a 3D view.", code = "INVALID_VIEW" };

            HashSet<int> elementIdsOpt = null;
            if (p["elementIds"] is JArray ja && ja.Count > 0)
                elementIdsOpt = new HashSet<int>(ja.Values<int>());

            bool includeLinked = p.Value<bool?>("includeLinked") ?? true;
            string unitsOut = (p.Value<string>("unitsOut") ?? "mm").Trim().ToLowerInvariant(); // "mm" | "feet"
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

            // Tolerances
            double curveTolFeet = p.Value<double?>("curveTol") ?? 1e-4; // feet
            double angTolDeg = p.Value<double?>("angTolDeg") ?? 1.0;

            try
            {
                var folder = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                if (File.Exists(outPath)) File.Delete(outPath);

                double scale = (unitsOut == "mm") ? 304.8 : 1.0;                  // feet -> target
                double curveTol = (unitsOut == "mm") ? curveTolFeet * 304.8 : curveTolFeet;
                double angTolRad = Math.Max(1e-12, angTolDeg * Math.PI / 180.0);

                var ctx = new BrepFirstExportContext(doc, view, elementIdsOpt, includeLinked,
                                                     unitsOut, layerMode, scale, curveTol, angTolRad);

                var exporter = new Rvt.CustomExporter(doc, ctx)
                {
                    IncludeGeometricObjects = true,
                    ShouldStopOnError = false
                };
                exporter.Export(view);

                var file = new Rfi.File3dm();
                file.Settings.ModelUnitSystem = (lenUnit == Autodesk.Revit.DB.UnitTypeId.Millimeters) ? Rhino.UnitSystem.Millimeters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Meters) ? Rhino.UnitSystem.Meters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Centimeters) ? Rhino.UnitSystem.Centimeters : (lenUnit == Autodesk.Revit.DB.UnitTypeId.Inches) ? Rhino.UnitSystem.Inches : Rhino.UnitSystem.Feet;

                ctx.EmitTo3dm(file);
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
                RevitLogger.Error("[export_view_3dm_brep] " + ex);
                return new { ok = false, msg = ex.Message, code = "EXCEPTION" };
            }
        }

        // ====================================================================
        // BrepFirstExportContext
        // ====================================================================
        private sealed class BrepFirstExportContext : Rvt.IExportContext
        {
            private readonly Rvt.Document _doc;
            private readonly Rvt.View3D _view;
            private readonly HashSet<int> _filterIds; // null => export all
            private readonly bool _includeLinked;
            private readonly string _unitsOut; // "mm" or "feet"
            private readonly string _layerMode; // "byCategory"|"byType"|"byElement"|"single"
            private readonly double _scale; // feet->(mm|feet)
            private readonly double _curveTol; // in out unit
            private readonly double _angTol;   // radians

            private readonly Stack<Rvt.Transform> _tStack = new Stack<Rvt.Transform>();
            private readonly Stack<Rvt.ElementId> _ownerStack = new Stack<Rvt.ElementId>();

            private readonly Dictionary<int, PerElement> _elements = new Dictionary<int, PerElement>();

            // stats
            private int _brepCount = 0, _meshCount = 0, _curveCount = 0, _vertCount = 0, _faceCount = 0;

            public BrepFirstExportContext(Rvt.Document doc, Rvt.View3D view,
                HashSet<int> filterIds, bool includeLinked,
                string unitsOut, string layerMode, double scale,
                double curveTol, double angTol)
            {
                _doc = doc; _view = view;
                _filterIds = filterIds; _includeLinked = includeLinked;
                _unitsOut = (unitsOut ?? "mm").Trim().ToLowerInvariant();
                _layerMode = string.IsNullOrWhiteSpace(layerMode) ? "byCategory" : layerMode.ToLowerInvariant();
                _scale = (scale > 0) ? scale : 1.0;
                _curveTol = Math.Max(1e-12, curveTol);
                _angTol = Math.Max(1e-12, angTol);
            }

            // ---- IExportContext ----
            public bool Start() { _tStack.Clear(); _ownerStack.Clear(); _tStack.Push(Rvt.Transform.Identity); return true; }
            public void Finish() { }
            public bool IsCanceled() => false;
            public void OnCancel() { }

            public Rvt.RenderNodeAction OnViewBegin(Rvt.ViewNode node) => Rvt.RenderNodeAction.Proceed;
            public void OnViewEnd(Rvt.ElementId elementId) { }

            public Rvt.RenderNodeAction OnElementBegin(Rvt.ElementId elementId)
            {
                if (_filterIds != null && !_filterIds.Contains(elementId.IntegerValue))
                {
                    _ownerStack.Push(Rvt.ElementId.InvalidElementId);
                    return Rvt.RenderNodeAction.Proceed;
                }
                _ownerStack.Push(elementId);
                return Rvt.RenderNodeAction.Proceed;
            }

            public void OnElementEnd(Rvt.ElementId elementId)
            {
                if (_ownerStack.Count == 0) return;
                var eid = _ownerStack.Pop();
                if (eid == Rvt.ElementId.InvalidElementId) return;

                // Prefer exporter-provided polymesh stream to ensure consistent instance/link transforms.
                // If needed, fall back to manual geometry traversal.
                bool preferExporterStream = true;
                if (preferExporterStream) return;

                try
                {
                    var e = _doc.GetElement(eid);
                    if (e == null) return;

                    Rvt.Transform T = _tStack.Peek();
                    var opt = new Rvt.Options { ComputeReferences = false, IncludeNonVisibleObjects = false, View = _view };
                    var ge = e.get_Geometry(opt);
                    if (ge == null) return;

                    if (!_elements.TryGetValue(eid.IntegerValue, out var pe))
                    {
                        pe = new PerElement
                        {
                            ElementId = eid.IntegerValue,
                            UniqueId = e.UniqueId,
                            Category = e.Category?.Name ?? "Unknown",
                            TypeName = TryTypeName(e),
                        };
                        _elements[pe.ElementId] = pe;
                    }

                    foreach (var obj in ge)
                        ConvertGeometryObject(pe, obj as Rvt.GeometryObject, T);
                }
                catch { /* ignore element-level failures */ }
            }

            public Rvt.RenderNodeAction OnInstanceBegin(Rvt.InstanceNode node)
            {
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return Rvt.RenderNodeAction.Proceed;
            }
            public void OnInstanceEnd(Rvt.InstanceNode node) { if (_tStack.Count > 1) _tStack.Pop(); }

            public Rvt.RenderNodeAction OnLinkBegin(Rvt.LinkNode node)
            {
                if (!_includeLinked) return Rvt.RenderNodeAction.Skip;
                var t = _tStack.Peek().Multiply(node.GetTransform());
                _tStack.Push(t);
                return Rvt.RenderNodeAction.Proceed;
            }
            public void OnLinkEnd(Rvt.LinkNode node) { if (_tStack.Count > 1) _tStack.Pop(); }

            public Rvt.RenderNodeAction OnFaceBegin(Rvt.FaceNode node) => Rvt.RenderNodeAction.Proceed;
            public void OnFaceEnd(Rvt.FaceNode node) { }

            public void OnMaterial(Rvt.MaterialNode node) { }
            public void OnMaterialEnd(Rvt.MaterialNode node) { }

            public void OnLight(Rvt.LightNode node) { }
            public void OnRPC(Rvt.RPCNode node) { }

            public void OnPolymesh(Rvt.PolymeshTopology pm)
            {
                // Mimic MeshExportContext: accumulate a unified mesh per element/material in world space.
                if (_ownerStack.Count == 0) return;
                var eid = _ownerStack.Peek();
                if (eid == Rvt.ElementId.InvalidElementId) return;

                var e = _doc.GetElement(eid);
                if (e == null) return;

                if (!_elements.TryGetValue(eid.IntegerValue, out var pe))
                {
                    pe = new PerElement
                    {
                        ElementId = eid.IntegerValue,
                        UniqueId = e?.UniqueId ?? string.Empty,
                        Category = e?.Category?.Name ?? "Unknown",
                        TypeName = TryTypeName(e)
                    };
                    _elements[pe.ElementId] = pe;
                }

                var t = _tStack.Peek();

                var pts = pm.GetPoints();
                var facets = pm.GetFacets();
                var world = new Rvt.XYZ[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    var w = t.OfPoint(pts[i]);
                    world[i] = new Rvt.XYZ(w.X * _scale, w.Y * _scale, w.Z * _scale);
                }

                var rmesh = new Rg.Mesh();
                foreach (var p in world) rmesh.Vertices.Add(p.X, p.Y, p.Z);
                for (int i = 0; i < facets.Count; i++)
                {
                    var f = facets[i];
                    if (f.V1 < 0 || f.V2 < 0 || f.V3 < 0) continue;
                    rmesh.Faces.AddFace(f.V1, f.V2, f.V3);
                }
                rmesh.Normals.ComputeNormals();
                rmesh.Compact();

                // Store as mesh for later Brep conversion per element
                pe.Meshes.Add(rmesh);
                _meshCount++;
                _vertCount += rmesh.Vertices.Count;
                _faceCount += rmesh.Faces.Count;
            }

            public bool AllowExportElement(Rvt.ElementId id) => true;

            // ---- Core conversion ----
            private void ConvertGeometryObject(PerElement pe, Rvt.GeometryObject go, Rvt.Transform T)
            {
                if (go == null) return;

                if (go is Rvt.GeometryInstance gi)
                {
                    var instGeo = gi.GetInstanceGeometry();
                    var IT = gi.Transform; // instance-local
                    var T2 = T.Multiply(IT);
                    foreach (var obj in instGeo)
                        ConvertGeometryObject(pe, obj as Rvt.GeometryObject, T2);
                    return;
                }

                if (go is Rvt.Solid solid && solid.Volume > 1e-12)
                {
                    ConvertSolid(pe, solid, T);
                    return;
                }

                if (go is Rvt.Mesh rvtMesh)
                {
                    AddMesh(pe, rvtMesh, T);
                    return;
                }

                if (go is Rvt.Curve crv)
                {
                    var rgc = ToRhinoCurve(crv, T, _scale, _curveTol, _angTol);
                    if (rgc != null) pe.Curves.Add(rgc);
                    _curveCount++;
                    return;
                }
            }

            private void ConvertSolid(PerElement pe, Rvt.Solid solid, Rvt.Transform T)
            {
                foreach (Rvt.Face f in solid.Faces)
                {
                    if (f is Rvt.PlanarFace pf)
                    {
                        try
                        {
                            var breps = PlanarFaceToBrep(pf, T);
                            if (breps != null)
                            {
                                foreach (var b in breps)
                                {
                                    pe.Breps.Add(b);
                                    _brepCount++;
                                    _faceCount += b.Faces.Count;
                                    _vertCount += b.Vertices.Count;
                                }
                                continue; // Brep化できた
                            }
                        }
                        catch { /* fallthrough to mesh */ }
                    }
                    // Fallback: triangulate and add as mesh
                    try
                    {
                        var m = f.Triangulate();
                        if (m != null) AddMesh(pe, m, T);
                    }
                    catch { /* ignore */ }
                }
            }

            // 平面フェイス → メッシュ化 → Brep.CreateFromMesh(mesh, true)
            private List<Rg.Brep> PlanarFaceToBrep(Rvt.PlanarFace pf, Rvt.Transform T)
            {
                var rm = pf.Triangulate(); // Rvt.Mesh
                if (rm == null || rm.Vertices.Count < 3 || rm.NumTriangles < 1) return null;

                var rmesh = new Rg.Mesh();
                int vcount = rm.Vertices.Count;
                for (int i = 0; i < vcount; i++)
                {
                    var v = T.OfPoint(rm.Vertices[i]);
                    rmesh.Vertices.Add(v.X * _scale, v.Y * _scale, v.Z * _scale);
                }
                int tcount = rm.NumTriangles;
                for (int i = 0; i < tcount; i++)
                {
                    var tri = rm.get_Triangle(i);
                    int a = (int)tri.get_Index(0); // uint -> int
                    int b = (int)tri.get_Index(1);
                    int c = (int)tri.get_Index(2);
                    rmesh.Faces.AddFace(a, b, c);
                }
                rmesh.Normals.ComputeNormals();
                rmesh.Compact();

                var brep = Rg.Brep.CreateFromMesh(rmesh, true);
                if (brep != null)
                {
                    return new List<Rg.Brep> { brep };
                }
                return null;
            }

            private void AddMesh(PerElement pe, Rvt.Mesh m, Rvt.Transform T)
            {
                var rmesh = new Rg.Mesh();
                int n = m.Vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    var v = T.OfPoint(m.Vertices[i]);
                    rmesh.Vertices.Add(v.X * _scale, v.Y * _scale, v.Z * _scale);
                }
                for (int i = 0; i < m.NumTriangles; i++)
                {
                    var tri = m.get_Triangle(i);
                    int a = (int)tri.get_Index(0); // uint -> int
                    int b = (int)tri.get_Index(1);
                    int c = (int)tri.get_Index(2);
                    rmesh.Faces.AddFace(a, b, c);
                }
                rmesh.Normals.ComputeNormals();
                rmesh.Compact();

                pe.Meshes.Add(rmesh);
                _meshCount++;
                _vertCount += rmesh.Vertices.Count;
                _faceCount += rmesh.Faces.Count;
            }

            // ---- Emit to .3dm ----
            public void EmitTo3dm(Rfi.File3dm file)
            {
                var layerMap = new Dictionary<string, int>();
                int EnsureLayer(string name)
                {
                    if (layerMap.TryGetValue(name, out int idx)) return idx;
                    var layer = new Rhino.DocObjects.Layer { Name = name };
                    file.AllLayers.Add(layer);               // rhino3dm: Add returns void
                    int newIndex = file.AllLayers.Count - 1;
                    layerMap[name] = newIndex;
                    return newIndex;
                }

                foreach (var kv in _elements)
                {
                    var pe = kv.Value;

                    string layerName = _layerMode switch
                    {
                        "byelement" => $"Element::{pe.ElementId}",
                        "bytype" => $"Type::{Safe(pe.TypeName)}",
                        "single" => "RevitExport",
                        _ => $"Category::{Safe(pe.Category)}", // default byCategory
                    };
                    int layerIndex = EnsureLayer(layerName);

                    // If no explicit Breps were captured, try convert aggregated meshes into a single Brep
                    if (pe.Breps.Count == 0 && pe.Meshes.Count > 0)
                    {
                        var combined = new Rg.Mesh();
                        foreach (var m in pe.Meshes) combined.Append(m);
                        combined.Normals.ComputeNormals();
                        combined.Compact();
                        var brep = Rg.Brep.CreateFromMesh(combined, true);
                        if (brep != null)
                        {
                            // rhino3dm (FileIO) には MergeCoplanarFaces は存在しないため、そのまま出力
                            pe.Breps.Add(brep);
                        }
                    }

                    foreach (var b in pe.Breps)
                    {
                        var attr = new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
                        SetMeta(attr, pe);
                        file.Objects.AddBrep(b, attr);
                    }

                    // Optionally also emit meshes if needed (keep disabled to reduce duplication)
                    //foreach (var m in pe.Meshes)
                    //{
                    //    var attr = new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
                    //    SetMeta(attr, pe);
                    //    file.Objects.AddMesh(m, attr);
                    //}

                    foreach (var c in pe.Curves)
                    {
                        var attr = new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
                        SetMeta(attr, pe);
                        file.Objects.AddCurve(c, attr);
                    }
                }
            }

            public object GetStats() => new
            {
                elements = _elements.Count,
                breps = _brepCount,
                meshes = _meshCount,
                curves = _curveCount,
                vertices = _vertCount,
                faces = _faceCount
            };

            private static void SetMeta(Rhino.DocObjects.ObjectAttributes attr, PerElement pe)
            {
                attr.SetUserString("Revit.ElementId", pe.ElementId.ToString());
                if (!string.IsNullOrEmpty(pe.UniqueId)) attr.SetUserString("Revit.UniqueId", pe.UniqueId);
                if (!string.IsNullOrEmpty(pe.Category)) attr.SetUserString("Revit.Category", pe.Category);
                if (!string.IsNullOrEmpty(pe.TypeName)) attr.SetUserString("Revit.TypeName", pe.TypeName);
            }

            private static string TryTypeName(Rvt.Element e)
            {
                try
                {
                    var tid = e?.GetTypeId();
                    if (tid != null && tid.IntegerValue > 0)
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

            // ---- Curve converter --------------------------------------------
            private Rg.Curve ToRhinoCurve(Rvt.Curve c, Rvt.Transform T, double scale, double tol, double angTol)
            {
                if (c == null) return null;

                if (c is Rvt.Line ln)
                {
                    var p0 = T.OfPoint(ln.GetEndPoint(0));
                    var p1 = T.OfPoint(ln.GetEndPoint(1));
                    return new Rg.LineCurve(
                        new Rg.Point3d(p0.X * scale, p0.Y * scale, p0.Z * scale),
                        new Rg.Point3d(p1.X * scale, p1.Y * scale, p1.Z * scale));
                }

                if (c is Rvt.Arc arc)
                {
                    var p0 = T.OfPoint(arc.GetEndPoint(0));
                    var p1 = T.OfPoint(arc.GetEndPoint(1));
                    var pm = T.OfPoint(arc.Evaluate(0.5, true));
                    var a0 = new Rg.Point3d(p0.X * scale, p0.Y * scale, p0.Z * scale);
                    var am = new Rg.Point3d(pm.X * scale, pm.Y * scale, pm.Z * scale);
                    var a1 = new Rg.Point3d(p1.X * scale, p1.Y * scale, p1.Z * scale);

                    var rarc = new Rg.Arc(a0, am, a1);   // 3-point arc
                    return new Rg.ArcCurve(rarc);        // ArcCurve(Arc)
                }

                // Fallback: tessellate to polyline
                var pts = c.Tessellate();
                if (pts == null || pts.Count < 2) return null;
                var pl = new Rg.Polyline(pts.Count);
                foreach (var q in pts)
                {
                    var w = T.OfPoint(q);
                    pl.Add(w.X * scale, w.Y * scale, w.Z * scale);
                }
                return new Rg.PolylineCurve(pl);
            }

            // ---- Per-element data -------------------------------------------
            private sealed class PerElement
            {
                public int ElementId;
                public string UniqueId;
                public string Category;
                public string TypeName;

                public readonly List<Rg.Brep> Breps = new List<Rg.Brep>();
                public readonly List<Rg.Mesh> Meshes = new List<Rg.Mesh>();
                public readonly List<Rg.Curve> Curves = new List<Rg.Curve>();
            }
        }
    }
}

