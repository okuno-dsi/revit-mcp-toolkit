#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    /// <summary>
    /// JSON-RPC: find_similar_by_factors
    /// Search current document for elements similar to given factors.
    /// Params: { categoryId?:int, level?:string, centroid:{xMm,yMm}, maxDistanceMm?:double, top?:int }
    /// Result: { ok, items:[{elementId,category,level,distanceMm}] }
    /// </summary>
    public class FindSimilarByFactorsCommand : IRevitCommandHandler
    {
        public string CommandName => "find_similar_by_factors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());
            int? categoryId = p.Value<int?>("categoryId");
            string levelName = (p.Value<string>("level") ?? string.Empty).Trim();
            var c = p["centroid"] as JObject;
            if (c == null) return ResultUtil.Err("centroid {xMm,yMm} is required.");
            double xMm = c.Value<double?>("xMm") ?? 0.0;
            double yMm = c.Value<double?>("yMm") ?? 0.0;
            double maxD = p.Value<double?>("maxDistanceMm") ?? double.MaxValue;
            int top = Math.Max(1, p.Value<int?>("top") ?? 5);

            IEnumerable<Element> candidates;
            if (categoryId.HasValue)
            {
                try
                {
                    var bic = (BuiltInCategory)categoryId.Value;
                    candidates = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .OfCategory(bic)
                        .ToElements();
                }
                catch
                {
                    candidates = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
                }
            }
            else
            {
                candidates = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
            }

            // Optional level filtering
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                candidates = candidates.Where(e =>
                {
                    try
                    {
                        var pid = e.get_Parameter(BuiltInParameter.LEVEL_PARAM) ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                        var lid = pid != null && pid.StorageType == StorageType.ElementId ? pid.AsElementId() : null;
                        var ln = lid != null ? (doc.GetElement(lid) as Level)?.Name : null;
                        return string.Equals(ln ?? string.Empty, levelName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });
            }

            var list = new List<object>();
            foreach (var e in candidates)
            {
                try
                {
                    var pt = CentroidMm(e);
                    var dx = pt.xMm - xMm; var dy = pt.yMm - yMm; var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= maxD)
                    {
                        string cat = e.Category?.Name ?? string.Empty;
                        string lv = string.Empty;
                        try
                        {
                            var pid = e.get_Parameter(BuiltInParameter.LEVEL_PARAM) ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                            var lid = pid != null && pid.StorageType == StorageType.ElementId ? pid.AsElementId() : null;
                            lv = lid != null ? (doc.GetElement(lid) as Level)?.Name ?? string.Empty : string.Empty;
                        }
                        catch { }
                        list.Add(new { elementId = e.Id.IntegerValue, category = cat, level = lv, distanceMm = Math.Round(d, 3) });
                    }
                }
                catch { }
            }

            var topN = list.OrderBy(o => ((dynamic)o).distanceMm).Take(top).ToList();
            return ResultUtil.Ok(new { ok = true, count = topN.Count, items = topN });
        }

        private static (double xMm, double yMm) CentroidMm(Element e)
        {
            try
            {
                if (e.Location is LocationPoint lp && lp.Point != null)
                    return (UnitHelper.FtToMm(lp.Point.X), UnitHelper.FtToMm(lp.Point.Y));
                var bb = e.get_BoundingBox(null);
                if (bb != null) { var c = (bb.Min + bb.Max) * 0.5; return (UnitHelper.FtToMm(c.X), UnitHelper.FtToMm(c.Y)); }
            }
            catch { }
            return (0, 0);
        }
    }
}

