// RevitMCPAddin/Commands/MEPOps/SetMepParameterCommand.cs (UnitHelper対応)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class SetMepParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_mep_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = doc.GetElement(new ElementId(p.Value<int>("elementId")));
            if (el == null) return new { ok = false, msg = "Element not found." };

            string paramName = p.Value<string>("paramName");
            var valTok = p["value"];
            if ((string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) || valTok == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid と value が必要です。" };

            var prm = ParamResolver.ResolveByPayload(el, p, out var resolvedBy);
            if (prm == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };

            using (var tx = new Transaction(doc, "Set MEP Param"))
            {
                tx.Start();
                var ok = UnitHelper.TrySetParameterByExternalValue(prm, valTok.ToObject<object>(), out var err);
                if (!ok)
                {
                    tx.RollBack();
                    return new { ok = false, msg = err ?? "Failed to set parameter." };
                }
                tx.Commit();
            }
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
