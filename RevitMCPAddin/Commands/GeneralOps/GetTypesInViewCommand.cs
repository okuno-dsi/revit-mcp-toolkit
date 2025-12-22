using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GeneralOps
{
    /// <summary>
    /// Returns unique ElementType ids used by elements visible in a view.
    /// Params:
    ///   - viewId: int (required)
    ///   - categoryIds?: int[] (optional prefilter)
    ///   - includeIndependentTags?: bool (default false)
    ///   - includeElementTypes?: bool (default false)
    ///   - modelOnly?: bool (default true) — exclude annotation/imports
    ///   - includeTypeInfo?: bool (default true) — include typeName/familyName
    ///   - includeCounts?: bool (default true) — include per-type instance count
    /// Result: { ok, totalCount, types: [ { typeId, count?, categoryId?, typeName?, familyName? } ] }
    /// </summary>
    public class GetTypesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_types_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = cmd.Params as JObject ?? new JObject();
            if (!(p["viewId"] is JToken vidTok))
                return new { ok = false, msg = "Missing parameter: viewId" };

            int viewId = vidTok.Value<int>();
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

            bool includeIndependentTags = p.Value<bool?>("includeIndependentTags") ?? false;
            bool includeElementTypes = p.Value<bool?>("includeElementTypes") ?? false;
            bool modelOnly = p.Value<bool?>("modelOnly") ?? true;
            bool includeTypeInfo = p.Value<bool?>("includeTypeInfo") ?? true;
            bool includeCounts = p.Value<bool?>("includeCounts") ?? true;

            var catIds = new HashSet<int>();
            foreach (var key in new[] { "categoryIds", "categories" })
            {
                if (p[key] is JArray arr)
                {
                    foreach (var t in arr)
                    {
                        try { catIds.Add(t.Value<int>()); } catch { }
                    }
                }
            }

            var collector = new FilteredElementCollector(doc, view.Id);
            if (!includeElementTypes)
                collector = collector.WhereElementIsNotElementType();

            if (catIds.Count > 0)
            {
                try
                {
                    var filters = catIds.Select(i => new ElementCategoryFilter(Autodesk.Revit.DB.ElementIdCompat.From(i))).Cast<ElementFilter>().ToList();
                    if (filters.Count == 1)
                        collector = collector.WherePasses(filters[0]);
                    else if (filters.Count > 1)
                        collector = collector.WherePasses(new LogicalOrFilter(filters));
                }
                catch { /* ignore invalid category ids */ }
            }

            var elems = collector.ToElements();

            var counts = new Dictionary<int, int>();
            var typeCat = new Dictionary<int, int?>();

            foreach (var e in elems)
            {
                if (!includeIndependentTags && e is IndependentTag) continue;
                try
                {
                    if (modelOnly)
                    {
                        var c = e.Category;
                        var ct = (c != null) ? c.CategoryType : CategoryType.Model;
                        if (ct != CategoryType.Model) continue;
                    }
                }
                catch { }

                ElementId tid;
                try { tid = e.GetTypeId(); } catch { continue; }
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                int t = tid.IntValue(); if (t <= 0) continue;

                if (!counts.ContainsKey(t)) counts[t] = 0;
                counts[t]++;

                if (!typeCat.ContainsKey(t))
                {
                    int? cid = null;
                    try { var c = e.Category; if (c != null) cid = c.Id.IntValue(); } catch { }
                    typeCat[t] = cid;
                }
            }

            var list = new List<object>(counts.Count);
            foreach (var kv in counts)
            {
                int t = kv.Key; int count = kv.Value;
                int? cid = typeCat.TryGetValue(t, out var tmp) ? tmp : null;
                if (includeTypeInfo)
                {
                    string typeName = null; string familyName = null;
                    try
                    {
                        var et = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(t)) as ElementType;
                        if (et != null)
                        {
                            typeName = et.Name;
                            try { familyName = et.FamilyName; } catch { }
                        }
                    }
                    catch { }

                    list.Add(new
                    {
                        typeId = t,
                        count = includeCounts ? (int?)count : null,
                        categoryId = cid,
                        typeName = typeName,
                        familyName = string.IsNullOrEmpty(familyName) ? null : familyName
                    });
                }
                else
                {
                    list.Add(new { typeId = t, count = includeCounts ? (int?)count : null, categoryId = cid });
                }
            }

            return new { ok = true, totalCount = counts.Count, types = list };
        }
    }
}



