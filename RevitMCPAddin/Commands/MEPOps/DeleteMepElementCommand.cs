// RevitMCPAddin/Commands/MEPOps/DeleteMepElementCommand.cs (UnitHelper対応)
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class DeleteMepElementCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_mep_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int elementId = p.Value<int>("elementId");
            var id = new ElementId(elementId);

            using var tx = new Transaction(doc, "Delete MEP Element");
            tx.Start();
            try
            {
                var deleted = doc.Delete(id);
                if (!deleted.Contains(id))
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to delete element {elementId}." };
                }
                tx.Commit();
                return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex)
            {
                tx.RollBack();
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
