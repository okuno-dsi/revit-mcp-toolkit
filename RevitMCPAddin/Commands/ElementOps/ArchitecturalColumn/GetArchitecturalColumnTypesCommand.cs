// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/GetArchitecturalColumnTypesCommand.cs
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class GetArchitecturalColumnTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_architectural_column_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(sym => sym?.Family?.FamilyCategory?.Id.IntValue() == (int)BuiltInCategory.OST_Columns)
                .ToList();

            var ordered = all
                .Select(s => new { s, name = s.Name ?? string.Empty, fam = s.Family?.Name ?? string.Empty, id = s.Id.IntValue() })
                .OrderBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.s)
                .ToList();

            int totalCount = ordered.Count;
            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(s => s.Name ?? string.Empty).ToList();
                return new { ok = true, totalCount, names, units = UnitHelper.DefaultUnitsMeta() };
            }

            if (idsOnly)
            {
                var ids = ordered.Skip(skip).Take(limit).Select(s => s.Id.IntValue()).ToList();
                return new { ok = true, totalCount, typeIds = ids, units = UnitHelper.DefaultUnitsMeta() };
            }

            var types = ordered.Skip(skip).Take(limit)
                .Select(sym => new { typeId = sym.Id.IntValue(), typeName = sym.Name ?? string.Empty, familyName = sym.Family?.Name ?? string.Empty })
                .ToList();

            return new { ok = true, totalCount, types, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

