using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class DeleteDoorCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_door";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var id = new ElementId(((JObject)cmd.Params).Value<int>("elementId"));

            using var tx = new Transaction(doc, "Delete Door");
            tx.Start();
            var deletedIds = doc.Delete(id);
            tx.Commit();

            bool success = deletedIds.Any(e => e.IntegerValue == id.IntegerValue);
            if (success)
            {
                return new { ok = true };
            }
            else
            {
                return new { ok = false, message = "Failed to delete door." };
            }
        }
    }
}
