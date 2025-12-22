// RevitMCPAddin/Commands/ElementOps/Window/DeleteWindowCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class DeleteWindowCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_window";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var id = Autodesk.Revit.DB.ElementIdCompat.From(((JObject)cmd.Params).Value<int>("elementId"));

            using var tx = new Transaction(doc, "Delete Window");
            tx.Start();
            var deletedIds = doc.Delete(id);
            tx.Commit();

            bool success = deletedIds.Any(e => e.IntValue() == id.IntValue());
            return success ? (object)new { ok = true } : new { ok = false, message = "Failed to delete window." };
        }
    }
}


