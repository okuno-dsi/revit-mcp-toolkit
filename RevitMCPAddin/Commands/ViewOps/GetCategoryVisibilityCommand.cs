// RevitMCPAddin/Commands/ViewOps/GetCategoryVisibilityCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// Get visibility state of categories in a view.
    /// Supports: categoryId / categoryIds / categoryName / categoryNames (single or multiple).
    /// Returns requested, results, skipped, errors.
    /// </summary>
    public class GetCategoryVisibilityCommand : IRevitCommandHandler
    {
        public string CommandName => "get_category_visibility";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int viewId = p.Value<int>("viewId");
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View
                       ?? throw new InvalidOperationException($"View not found: {viewId}");

            var resolveResult = ResolveCategories(doc, p);
            var requested = resolveResult.RequestedCount;

            if (resolveResult.Resolved.Count == 0 && resolveResult.Skipped.Count == 0)
            {
                return new { ok = false, viewId, message = "Specify 'categoryId(s)' or 'categoryName(s)'." };
            }

            var results = new List<object>();
            var errors = new List<object>();

            foreach (var cid in resolveResult.Resolved)
            {
                try
                {
                    bool isHidden = view.GetCategoryHidden(cid);
                    results.Add(new { categoryId = cid.IntValue(), visible = !isHidden });
                }
                catch (Exception ex)
                {
                    RevitLogger.Error($"GetCategoryHidden failed for {cid.IntValue()}", ex);
                    errors.Add(new { categoryId = cid.IntValue(), reason = ex.Message });
                }
            }

            return new
            {
                ok = true,
                viewId,
                requested,
                results,
                skipped = resolveResult.Skipped,
                errors
            };
        }

        private static (List<ElementId> Resolved, List<object> Skipped, int RequestedCount) ResolveCategories(Document doc, JObject p)
        {
            var resolved = new List<ElementId>();
            var skipped = new List<object>();
            int requestedCount = 0;

            var allCats = doc.Settings.Categories.Cast<Category>().ToList();
            var byId = allCats.ToDictionary(c => c.Id.IntValue(), c => c);
            var byName = allCats.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (p.TryGetValue("categoryId", out var catIdToken))
            {
                requestedCount++;
                int id = catIdToken.Value<int>();
                if (byId.TryGetValue(id, out var cat))
                    resolved.Add(cat.Id);
                else
                    skipped.Add(new { categoryId = id, reason = "CategoryId not found" });
            }

            if (p.TryGetValue("categoryIds", out var catIdsToken) && catIdsToken is JArray idArr)
            {
                requestedCount += idArr.Count;
                foreach (var t in idArr)
                {
                    int id = (int)t;
                    if (byId.TryGetValue(id, out var cat))
                        resolved.Add(cat.Id);
                    else
                        skipped.Add(new { categoryId = id, reason = "CategoryId not found" });
                }
            }

            if (p.TryGetValue("categoryName", out var catNameToken))
            {
                requestedCount++;
                string name = catNameToken.Value<string>();
                if (!string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out var cat))
                    resolved.Add(cat.Id);
                else
                    skipped.Add(new { categoryName = name, reason = "Category name not found" });
            }

            if (p.TryGetValue("categoryNames", out var catNamesToken) && catNamesToken is JArray nameArr)
            {
                requestedCount += nameArr.Count;
                foreach (var t in nameArr)
                {
                    string name = (string)t;
                    if (!string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out var cat))
                        resolved.Add(cat.Id);
                    else
                        skipped.Add(new { categoryName = name, reason = "Category name not found" });
                }
            }

            return (resolved, skipped, requestedCount);
        }
    }
}


