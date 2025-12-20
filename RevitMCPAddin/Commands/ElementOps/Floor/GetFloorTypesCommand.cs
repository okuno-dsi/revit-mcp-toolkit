// File: RevitMCPAddin/Commands/ElementOps/FloorOps/GetFloorTypesCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class GetFloorTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存ページング（互換）
            int skipLegacy = p.Value<int?>("skip") ?? 0;
            int countLegacy = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + 軽量
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? countLegacy);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skipLegacy);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .WhereElementIsElementType()
                .Cast<FloorType>()
                .ToList();

            IEnumerable<FloorType> q = allTypes;

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(t => string.Equals(t.Name ?? "", typeName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(familyName))
                q = q.Where(t => string.Equals(t.FamilyName ?? "", familyName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(t => (t.Name ?? "").IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);

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
                .Select(t => new { t, fam = t.FamilyName ?? "", name = t.Name ?? "", id = t.Id.IntegerValue })
                .OrderBy(x => x.fam)
                .ThenBy(x => x.name)
                .ThenBy(x => x.id)
                .Select(x => x.t)
                .ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(t => t.Name ?? "").ToList();
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
                var typeIds = ordered.Skip(skip).Take(limit).Select(t => t.Id.IntegerValue).ToList();
                return new
                {
                    ok = true,
                    totalCount,
                    typeIds,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var list = ordered
                .Skip(skip)
                .Take(limit)
                .Select(ft => new
                {
                    typeId = ft.Id.IntegerValue,
                    uniqueId = ft.UniqueId,
                    typeName = ft.Name ?? "",
                    familyName = ft.FamilyName ?? ""
                })
                .ToList();

            return new
            {
                ok = true,
                totalCount,
                floorTypes = list,
                inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
