// RevitMCPAddin/Commands/MEPOps/ChangeMepElementTypeCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class ChangeMepElementTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_mep_element_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var elemId = new ElementId(p.Value<int>("elementId"));
            var newTypeId = new ElementId(p.Value<int>("newTypeId"));

            var el = doc.GetElement(elemId);
            if (el == null) return new { ok = false, msg = $"Element not found: {elemId.IntegerValue}" };

            using (var tx = new Transaction(doc, "Change MEP Element Type"))
            {
                tx.Start();
                el.ChangeTypeId(newTypeId);
                tx.Commit();
            }
            return new
            {
                ok = true,
                elementId = el.Id.IntegerValue,
                typeId = el.GetTypeId().IntegerValue,
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }
}
