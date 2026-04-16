// RevitMCPAddin/Commands/ScheduleOps/GetSchedulesCommand.cs (UnitHelper対応)
using System;
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
            var doc = DocumentResolver.ResolveDocument(uiapp, cmd);
            if (doc == null)
                return new { ok = false, msg = "Target document not found." };

            var activeDoc = uiapp.ActiveUIDocument?.Document;
            var activeScheduleId =
                activeDoc != null &&
                string.Equals(activeDoc.PathName ?? string.Empty, doc.PathName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                activeDoc.ActiveView is ViewSchedule activeSchedule
                    ? activeSchedule.Id.IntValue()
                    : 0;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate)
                .OrderBy(vs => vs.Name)
                .Select(vs =>
                {
                    var analysis = ScheduleRoundtripExcelUtil.AnalyzeScheduleRoundtripSupport(doc, vs);
                    return new
                    {
                        scheduleViewId = vs.Id.IntValue(),
                        title = vs.Name,
                        categoryName = analysis.CategoryName,
                        isActive = vs.Id.IntValue() == activeScheduleId,
                        supportStatus = analysis.StatusCode,
                        supportReasonCode = analysis.ReasonCode,
                        supportReason = analysis.Reason,
                        suggestedMode = analysis.SuggestedMode,
                        visibleColumnCount = analysis.VisibleColumnCount
                    };
                })
                .ToList();
            return new { ok = true, schedules, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

