// File: RevitMCPAddin/Commands/ElementOps/Door/GetDoorTypesCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class GetDoorTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_door_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            // Legacy paging params (backward compatible)
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // New shape/paging
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? count);
            int skip2 = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = allTypes;

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(s => string.Equals(s.Name ?? "", typeName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(familyName))
                q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? "").IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);

            var filtered = q.ToList();
            int totalCount = filtered.Count;

            if (summaryOnly || limit == 0)
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };

            var ordered = filtered
                .Select(s => new { s, fam = s.Family?.Name ?? "", name = s.Name ?? "", id = s.Id.IntegerValue })
                .OrderBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.s)
                .ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip2).Take(limit).Select(s => s.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (idsOnly)
            {
                var ids = ordered.Skip(skip2).Take(limit).Select(s => s.Id.IntegerValue).ToList();
                return new
                {
                    ok = true,
                    totalCount,
                    typeIds = ids,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var list = ordered.Skip(skip2).Take(limit).Select(s => new
            {
                typeId = s.Id.IntegerValue,
                uniqueId = s.UniqueId,
                typeName = s.Name ?? "",
                familyName = s.Family?.Name ?? ""
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                types = list,
                inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
