// RevitMCPAddin/Commands/ScheduleOps/GetSchedulesCommand.cs (UnitHelper対応)
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class GetSchedulesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_schedules";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            Document doc = uiapp.ActiveUIDocument.Document;
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Select(vs =>
                {
                    var catId = vs.Definition.CategoryId;
                    var category = doc.Settings.Categories
                        .Cast<Category>()
                        .FirstOrDefault(c => c.Id == catId);
                    return new
                    {
                        scheduleViewId = vs.Id.IntegerValue,
                        title = vs.Name,
                        categoryName = category?.Name ?? string.Empty
                    };
                })
                .ToList();
            return new { ok = true, schedules, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
