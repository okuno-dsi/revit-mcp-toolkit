#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    public class SummarizeElementsByCategoryCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_elements_by_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            bool includeAnnotations = false;
            try { includeAnnotations = (cmd.Params?.Value<bool?>("includeAnnotations") ?? false); } catch { }

            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                var c = e.Category; if (c == null) continue;
                if (!includeAnnotations && c.CategoryType == CategoryType.Annotation) continue;
                var key = c.Name ?? "(No category)";
                if (dict.TryGetValue(key, out var cur)) dict[key] = cur + 1; else dict[key] = 1;
            }

            var items = dict.Select(kv => new { categoryName = kv.Key, count = kv.Value })
                            .OrderByDescending(x => x.count)
                            .ToList();
            return new { ok = true, items = items, total = items.Sum(x => x.count) };
        }
    }
}

