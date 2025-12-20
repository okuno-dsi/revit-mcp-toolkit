#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// snapshot_view_elements: Capture elements visible in a view with essential metadata for diffs.
    /// Params:
    ///   - viewId: int (required)
    ///   - categoryIds?: int[] (optional)
    ///   - includeTypeParams?: [{ name: string }] (optional)
    ///   - includeAnalytic?: bool (default: false)  // include wire endpoints for LocationCurve
    ///   - includeHidden?: bool (default: false)     // best-effort; view collector is visibility-scoped
    ///   - page?: { startIndex:int, batchSize:int }
    /// Returns: { ok, project:{name,number}, port:int, view:{id,name}, count, elements:[...], typeParameters? }
    /// Units: millimeters for coordinates/lengths
    /// </summary>
    public class SnapshotViewElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "snapshot_view_elements";

        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("viewId", out var vTok))
                return new { ok = false, code = "NO_VIEW", msg = "Missing parameter: viewId" };

            var view = doc.GetElement(new ElementId(vTok.Value<int>())) as View;
            if (view == null)
                return new { ok = false, code = "NO_VIEW", msg = $"View not found: {vTok}" };

            var categoryIds = (p["categoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            bool includeAnalytic = p.Value<bool?>("includeAnalytic") ?? false;
            bool includeHidden = p.Value<bool?>("includeHidden") ?? false; // best-effort only

            var page = p["page"] as JObject;
            int startIndex = page?.Value<int?>("startIndex") ?? 0;
            int batchSize = page?.Value<int?>("batchSize") ?? int.MaxValue;

            var reqTypeParams = new List<string>();
            if (p.TryGetValue("includeTypeParams", out var tpTok) && tpTok is JArray tparr)
            {
                foreach (var o in tparr.OfType<JObject>())
                {
                    var name = o.Value<string>("name");
                    if (!string.IsNullOrWhiteSpace(name)) reqTypeParams.Add(name.Trim());
                }
            }

            try
            {
                // Collector scoped to the view (visible elements). If includeHidden, we switch to a doc-wide
                // collector then filter by category and view bbox (best-effort) to include potentially hidden ones.
                IEnumerable<Element> Query()
                {
                    if (!includeHidden)
                    {
                        var c = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType();
                        return c.ToElements().Where(e => e != null);
                    }
                    else
                    {
                        // Best-effort: doc-wide elements in categories; do not include ElementTypes.
                        var c = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                        return c.ToElements().Where(e => e != null);
                    }
                }

                bool HasCategory(Element e)
                {
                    try { return e.Category != null && e.Category.Id != null; } catch { return false; }
                }

                int GetCatId(Element e)
                {
                    try { return e.Category?.Id?.IntegerValue ?? 0; } catch { return 0; }
                }

                // Elements collection
                var all = Query()
                    .Where(e => HasCategory(e))
                    .Where(e => categoryIds.Count == 0 || categoryIds.Contains(GetCatId(e)))
                    .ToList();

                int total = all.Count;
                if (startIndex < 0) startIndex = 0;
                if (batchSize < 0) batchSize = 0;
                var slice = (batchSize == int.MaxValue) ? all.Skip(startIndex) : all.Skip(startIndex).Take(batchSize);

                // Type parameter aggregation bucket
                var typeParamsOut = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                bool wantTypeParams = reqTypeParams.Count > 0;

                var elementsOut = new List<object>();
                foreach (var e in slice)
                {
                    int eid = e.Id.IntegerValue;
                    string uid = e.UniqueId ?? string.Empty;
                    int catId = GetCatId(e);

                    // Type info
                    int typeId = 0; string typeName = string.Empty; string familyName = string.Empty;
                    try
                    {
                        var tid = e.GetTypeId();
                        if (tid != null && tid.IntegerValue > 0)
                        {
                            typeId = tid.IntegerValue;
                            if (doc.GetElement(tid) is ElementType et)
                            {
                                typeName = et.Name ?? string.Empty;
                                try { familyName = (et.FamilyName ?? string.Empty); } catch { }
                            }
                        }
                    }
                    catch { }

                    // BBox (in view if available); centroid fallback
                    XYZ min = null, max = null; XYZ centroid = null;
                    try
                    {
                        var bb = e.get_BoundingBox(view);
                        if (bb == null && includeHidden)
                            bb = e.get_BoundingBox(null);
                        if (bb != null)
                        {
                            min = bb.Min; max = bb.Max;
                            centroid = new XYZ((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);
                        }
                    }
                    catch { }
                    if (centroid == null)
                    {
                        try
                        {
                            if (e.Location is LocationPoint lp) centroid = lp.Point;
                            else if (e.Location is LocationCurve lc && lc.Curve != null)
                            {
                                var p0 = lc.Curve.GetEndPoint(0);
                                var p1 = lc.Curve.GetEndPoint(1);
                                centroid = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, (p0.Z + p1.Z) * 0.5);
                            }
                        }
                        catch { }
                    }

                    // Analytic wire endpoints
                    object analytic = null;
                    if (includeAnalytic)
                    {
                        try
                        {
                            if (e.Location is LocationCurve lc && lc.Curve != null)
                            {
                                var a = lc.Curve.GetEndPoint(0);
                                var b = lc.Curve.GetEndPoint(1);
                                analytic = new
                                {
                                    wire = new
                                    {
                                        a = new { x = Math.Round(FtToMm(a.X), 3), y = Math.Round(FtToMm(a.Y), 3), z = Math.Round(FtToMm(a.Z), 3) },
                                        b = new { x = Math.Round(FtToMm(b.X), 3), y = Math.Round(FtToMm(b.Y), 3), z = Math.Round(FtToMm(b.Z), 3) }
                                    }
                                };
                            }
                        }
                        catch { }
                    }

                    object bboxObj = null;
                    if (min != null && max != null)
                    {
                        bboxObj = new
                        {
                            min = new { x = Math.Round(FtToMm(min.X), 3), y = Math.Round(FtToMm(min.Y), 3), z = Math.Round(FtToMm(min.Z), 3) },
                            max = new { x = Math.Round(FtToMm(max.X), 3), y = Math.Round(FtToMm(max.Y), 3), z = Math.Round(FtToMm(max.Z), 3) }
                        };
                    }

                    object coordObj = null;
                    if (centroid != null)
                    {
                        coordObj = new { x = Math.Round(FtToMm(centroid.X), 3), y = Math.Round(FtToMm(centroid.Y), 3), z = Math.Round(FtToMm(centroid.Z), 3) };
                    }

                    // assemble element row
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["elementId"] = eid,
                        ["uniqueId"] = uid,
                        ["categoryId"] = catId,
                        ["familyName"] = familyName,
                        ["typeName"] = typeName,
                        ["typeId"] = typeId,
                    };
                    if (bboxObj != null) row["bboxMm"] = bboxObj;
                    if (coordObj != null) row["coordinatesMm"] = coordObj;
                    if (analytic != null) row["analytic"] = analytic;

                    elementsOut.Add(row);

                    // collect type params (by typeId) if requested
                    if (wantTypeParams && typeId > 0)
                    {
                        var key = typeId.ToString();
                        if (!typeParamsOut.ContainsKey(key))
                        {
                            var tpObj = BuildTypeParamPayload(doc, e.GetTypeId(), reqTypeParams);
                            if (tpObj != null) typeParamsOut[key] = tpObj;
                        }
                    }
                }

                // Project meta
                string projectName = string.Empty, projectNumber = string.Empty, projectGuid = string.Empty;
                try
                {
                    var pi = doc.ProjectInformation;
                    if (pi != null)
                    {
                        projectName = pi.Name ?? string.Empty;
                        try { projectNumber = pi.Number ?? string.Empty; } catch { }
                        try { projectGuid = pi.UniqueId ?? string.Empty; } catch { }
                    }
                }
                catch { }

                int port = PortLocator.GetCurrentPortOrDefault();

                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ok"] = true,
                    ["project"] = new { name = projectName, number = projectNumber, guid = projectGuid },
                    ["port"] = port,
                    ["view"] = new { id = view.Id.IntegerValue, name = view.Name ?? string.Empty },
                    ["count"] = total,
                    ["elements"] = elementsOut
                };
                if (typeParamsOut.Count > 0) result["typeParameters"] = typeParamsOut;

                // note: includeHidden is best-effort only
                if (includeHidden)
                {
                    result["issues"] = new[] { new { code = "INCLUDE_HIDDEN_BEST_EFFORT", msg = "includeHidden は限定的サポートです (doc-wide query)。" } };
                }

                return result;
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "EXCEPTION", msg = ex.Message };
            }
        }

        private static object BuildTypeParamPayload(Document doc, ElementId typeId, List<string> names)
        {
            try
            {
                var et = doc.GetElement(typeId) as ElementType;
                if (et == null) return null;

                var pvals = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var pdisp = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                foreach (Parameter p in et.Parameters)
                {
                    var n = p?.Definition?.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (names.Count > 0 && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;

                    try
                    {
                        object v = null; string units = null;
                        switch (p.StorageType)
                        {
                            case StorageType.String: v = p.AsString() ?? string.Empty; break;
                            case StorageType.Integer: v = p.AsInteger(); break;
                            case StorageType.ElementId: v = p.AsElementId()?.IntegerValue ?? 0; break;
                            case StorageType.Double:
                                // convert length-like doubles to mm; otherwise keep AsValueString
                                try
                                {
                                    var spec = p.Definition.GetDataType();
                                    if (spec == SpecTypeId.Length)
                                    {
                                        v = Math.Round(FtToMm(p.AsDouble()), 3);
                                        units = "mm";
                                    }
                                    else
                                    {
                                        v = p.AsDouble();
                                    }
                                }
                                catch { v = p.AsDouble(); }
                                break;
                        }

                        pvals[n] = (units == null) ? v : (object)new { value = v, units };
                        var disp = p.AsValueString() ?? p.AsString() ?? string.Empty;
                        pdisp[n] = disp;
                    }
                    catch { }
                }

                return new { @params = pvals, display = pdisp };
            }
            catch { return null; }
        }
    }
}
