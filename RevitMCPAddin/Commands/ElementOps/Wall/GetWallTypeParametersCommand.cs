// RevitMCPAddin/Commands/ElementOps/Wall/GetWallTypeParametersCommand.cs
// UnitHelper化: ResolveUnitsMode + MapParameter で統一
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class GetWallTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            WallType wallType = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");

            if (typeId > 0) wallType = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as WallType;
            else if (!string.IsNullOrWhiteSpace(typeName))
                wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                           .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
            else
                return new { ok = false, msg = "Parameter 'typeId' or 'typeName' is required." };

            if (wallType == null)
                return new { ok = false, msg = $"WallType not found: {(typeId > 0 ? typeId.ToString() : typeName)}" };

            typeId = wallType.Id.IntValue();
            typeName = wallType.Name ?? string.Empty;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            var allParams = wallType.Parameters?.Cast<Parameter>()
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa)
                .ToList() ?? new System.Collections.Generic.List<Parameter>();

            int totalCount = allParams.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
            {
                return new
                {
                    ok = true,
                    typeId,
                    typeName,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            var mode = UnitHelper.ResolveUnitsMode(doc, p);
            var list = allParams.Skip(skip).Take(count)
                .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                .ToList();

            return new
            {
                ok = true,
                typeId,
                typeName,
                totalCount,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta(),
                parameters = list
            };
        }
    }
}


