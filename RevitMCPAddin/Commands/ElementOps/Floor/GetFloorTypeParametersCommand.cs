// RevitMCPAddin/Commands/ElementOps/FloorOps/GetFloorTypeParametersCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class GetFloorTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // typeId/typeName(+familyName) or element → type
            FloorType ft = null;
            int typeId = p.Value<int?>("floorTypeId") ?? p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0) ft = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FloorType;
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).WhereElementIsElementType()
                        .Cast<FloorType>().Where(t => string.Equals(t.Name ?? "", typeName, System.StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(t => string.Equals(t.FamilyName ?? "", familyName, System.StringComparison.OrdinalIgnoreCase));
                ft = q.OrderBy(t => t.FamilyName ?? "").ThenBy(t => t.Name ?? "").FirstOrDefault();
            }
            else
            {
                Element inst = null;
                int eid = p.Value<int?>("elementId") ?? 0;
                string uid = p.Value<string>("uniqueId");
                if (eid > 0) inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) inst = doc.GetElement(uid);
                if (inst is Autodesk.Revit.DB.Floor f) ft = f.FloorType;
            }

            if (ft == null) return new { ok = false, msg = "FloorType が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (ft.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new
                {
                    ok = true,
                    typeId = ft.Id.IntValue(),
                    uniqueId = ft.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    typeId = ft.Id.IntValue(),
                    uniqueId = ft.UniqueId,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
                list.Add(UnitHelper.MapParameter(pa, doc, UnitsMode.SI, includeDisplay: true, includeRaw: true));

            return new
            {
                ok = true,
                typeId = ft.Id.IntValue(),
                uniqueId = ft.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}


