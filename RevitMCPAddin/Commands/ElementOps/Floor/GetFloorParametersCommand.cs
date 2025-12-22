// RevitMCPAddin/Commands/ElementOps/FloorOps/GetFloorParametersCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class GetFloorParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId / uniqueId 両対応
            Element el = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) el = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) el = doc.GetElement(uniqueId);
            if (el == null) return new { ok = false, msg = "Floor 要素が見つかりません（elementId/uniqueId）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (el.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new
                {
                    ok = true,
                    elementId = el.Id.IntValue(),
                    uniqueId = el.UniqueId,
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
                    elementId = el.Id.IntValue(),
                    uniqueId = el.UniqueId,
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
                elementId = el.Id.IntValue(),
                uniqueId = el.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}


