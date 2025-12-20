// RevitMCPAddin/Commands/ScheduleOps/DeleteScheduleCommand.cs (UnitHelper対応)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class DeleteScheduleCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_schedule";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("scheduleViewId");

            Document doc = uiapp.ActiveUIDocument.Document;
            var elem = doc.GetElement(new ElementId(id));
            if (elem == null)
                return new { ok = false, message = $"ScheduleView {id} not found.", units = UnitHelper.DefaultUnitsMeta() };

            using var tx = new Transaction(doc, "Delete Schedule");
            tx.Start();
            doc.Delete(elem.Id);
            tx.Commit();

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
