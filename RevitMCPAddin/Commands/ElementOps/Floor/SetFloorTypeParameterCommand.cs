// RevitMCPAddin/Commands/ElementOps/FloorOps/SetFloorTypeParameterCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class SetFloorTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_floor_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("typeId", out var typeIdToken))
                throw new InvalidOperationException("Parameter 'typeId' is required.");
            var ft = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeIdToken.Value<int>())) as FloorType
                     ?? throw new InvalidOperationException($"FloorType not found: {typeIdToken.Value<int>()}");

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, message = "paramName or builtInName/builtInId/guid is required." };
            var prm = ParamResolver.ResolveByPayload(ft, p, out var resolvedBy);
            if (prm == null) return new { ok = false, message = $"Parameter not found (name/builtIn/guid)." };
            if (prm.IsReadOnly) return new { ok = false, message = $"Parameter '{prm.Definition?.Name}' is read-only." };

            if (!p.TryGetValue("value", out var valueToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            using (var tx = new Transaction(doc, $"Set FloorType Parameter {prm.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                string? err;
                var ok = UnitHelper.TrySetParameterByExternalValue(prm, (valueToken as JValue)?.Value, out err);
                if (!ok) { tx.RollBack(); return new { ok = false, message = err ?? "Failed to set parameter." }; }
                tx.Commit();
            }
            return new { ok = true };
        }
    }
}

