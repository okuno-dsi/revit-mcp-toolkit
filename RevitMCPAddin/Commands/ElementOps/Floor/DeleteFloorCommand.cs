// DeleteFloorCommand.cs
using ARDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class DeleteFloorCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_floor";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            ARDB.Document doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var id = new ARDB.ElementId(p.Value<int>("elementId"));

            if (doc.GetElement(id) == null)
                return new { ok = false, msg = "Floor not found." };

            using (var tx = new ARDB.Transaction(doc, "Delete Floor"))
            {
                tx.Start();
                doc.Delete(id);
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
