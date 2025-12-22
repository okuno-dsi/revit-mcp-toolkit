// RevitMCPAddin/Commands/ElementOps/Wall/GetWallParameterCommand.cs
// UnitHelper化: ResolveUnitsMode + MapParameter で統一
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class GetWallParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            Autodesk.Revit.DB.Wall wall = null;
            int elementId = p.Value<int?>("elementId") ?? p.Value<int?>("wallId") ?? 0;
            if (elementId > 0) wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as Autodesk.Revit.DB.Wall;
            else
            {
                var uid = p.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid)) wall = doc.GetElement(uid) as Autodesk.Revit.DB.Wall;
                if (wall != null) elementId = wall.Id.IntValue();
            }
            if (wall == null) return new { ok = false, msg = "Wall not found. Provide elementId/wallId or uniqueId." };

            Parameter param = null;
            int paramId = p.Value<int?>("paramId") ?? 0;
            if (paramId > 0)
            {
                foreach (Parameter pa in wall.Parameters)
                {
                    if (pa?.Id?.IntValue() == paramId) { param = pa; break; }
                }
                if (param == null) return new { ok = false, elementId, msg = $"Parameter id {paramId} not found." };
            }
            else
            {
                var paramName = p.Value<string>("paramName");
                if (string.IsNullOrWhiteSpace(paramName))
                    return new { ok = false, elementId, msg = "Parameter 'paramName' or 'paramId' is required." };
                param = wall.LookupParameter(paramName);
                if (param == null) return new { ok = false, elementId, msg = $"Parameter '{paramName}' not found." };
            }

            var mode = UnitHelper.ResolveUnitsMode(doc, p);
            var mapped = UnitHelper.MapParameter(param, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3);

            return new
            {
                ok = true,
                elementId,
                uniqueId = wall.UniqueId ?? string.Empty,
                parameter = mapped,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }
}


