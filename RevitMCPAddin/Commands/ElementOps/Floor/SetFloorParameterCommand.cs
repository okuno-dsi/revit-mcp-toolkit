// RevitMCPAddin/Commands/ElementOps/FloorOps/SetFloorParameterCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class SetFloorParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_floor_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var idToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            var elementId = Autodesk.Revit.DB.ElementIdCompat.From(idToken.Value<int>());
            var el = doc.GetElement(elementId)
                     ?? throw new InvalidOperationException($"Floor not found: {elementId.IntValue()}");

            var prm = ParamResolver.ResolveByPayload(el, p, out var resolvedBy)
                      ?? throw new InvalidOperationException("Parameter not found (name/builtIn/guid).");
            if (prm.IsReadOnly) return new { ok = false, message = $"Parameter '{prm.Definition?.Name}' is read-only." };

            if (!p.TryGetValue("value", out var valToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            using (var tx = new Transaction(doc, $"Set Floor Parameter {prm.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                string? err;
                // ★ SI入力（mm/m2/m3/deg など）→内部(ft/rad/…)は UnitHelper が自動判定
                var ok = UnitHelper.TrySetParameterByExternalValue(prm, (valToken as JValue)?.Value, out err);
                if (!ok) { tx.RollBack(); return new { ok = false, message = err ?? "Failed to set parameter." }; }
                tx.Commit();
            }
            return new { ok = true };
        }
    }
}


