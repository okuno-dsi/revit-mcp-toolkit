#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnalysisOps
{
    public class SummarizeFamilyTypesByCategoryCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_family_types_by_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var result = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
            var instances = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var inst in instances)
            {
                var typeId = inst.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;
                var cat = inst.Category; if (cat == null) continue;
                var key = cat.Name ?? "(No category)";
                if (!result.TryGetValue(key, out var set)) { set = new HashSet<ElementId>(); result[key] = set; }
                set.Add(typeId);
            }

            var items = result.Select(kv => new { categoryName = kv.Key, typeCount = kv.Value.Count })
                              .OrderByDescending(x => x.typeCount)
                              .ToList();
            return new { ok = true, items };
        }
    }
}

