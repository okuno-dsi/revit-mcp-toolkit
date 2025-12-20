using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class UpdateWallTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_wall_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("typeId", out var tidToken))
                throw new ArgumentException("Parameter 'typeId' is required.");
            int typeId = tidToken.Value<int>();

            var wallType = doc.GetElement(new ElementId(typeId)) as WallType;
            if (wallType == null) return new { ok = false, msg = $"WallType not found: {typeId}" };

            // Resolve parameter by builtInId/builtInName/guid/name (fallback)
            var param = ParamResolver.ResolveByPayload(wallType, p, out var resolvedBy);
            if (param == null)
            {
                string paramName = p.Value<string>("paramName") ?? p.Value<string>("builtInName") ?? p.Value<string>("guid") ?? "";
                return new { ok = false, msg = $"Parameter not found (name/builtIn/guid): {paramName}" };
            }

            if (!p.TryGetValue("value", out var valToken))
                throw new ArgumentException("Parameter 'value' is required.");

            using (var tx = new Transaction(doc, $"Set WallType Param {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(param, valToken, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "Failed to set parameter." };
                }
                tx.Commit();
            }

            return new { ok = true, typeId, resolvedBy, inputUnits = UnitHelper.DefaultUnitsMeta(), internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" } };
        }
    }
}
