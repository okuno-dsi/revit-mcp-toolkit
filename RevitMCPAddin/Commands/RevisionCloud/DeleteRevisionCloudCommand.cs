// File: RevitMCPAddin/Commands/RevisionCloud/DeleteRevisionCloudCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class DeleteRevisionCloudCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_revision_cloud";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int id = ((JObject)cmd.Params).Value<int>("elementId");

            using (var tx = new Transaction(doc, "Delete Revision Cloud"))
            {
                tx.Start();
                doc.Delete(new ElementId(id));
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
