// RevitMCPAddin/Commands/ScheduleOps/UpdateScheduleFiltersCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class UpdateScheduleFiltersCommand : IRevitCommandHandler
    {
        public string CommandName => "update_schedule_filters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("scheduleViewId");
            var filters = p["filters"]?.ToObject<List<JObject>>()
                          ?? throw new ArgumentException("filters is required");

            Document doc = uiapp.ActiveUIDocument.Document;
            var vs = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ViewSchedule;
            if (vs == null)
                return new { ok = false, message = $"ScheduleView {id} not found.", units = UnitHelper.DefaultUnitsMeta() };

            using var tx = new Transaction(doc, "Update Schedule Filters");
            tx.Start();

            var def = vs.Definition;
            def.ClearFilters();

            foreach (var jf in filters)
            {
                string fieldName = jf.Value<string>("field");
                int fieldParamId = jf.Value<int?>("fieldParamId") ?? int.MinValue;
                var filterType = jf.Value<string>("operator") switch
                {
                    "Equals" => ScheduleFilterType.Equal,
                    "BeginsWith" => ScheduleFilterType.BeginsWith,
                    "Contains" => ScheduleFilterType.Contains,
                    "NotEquals" => ScheduleFilterType.NotEqual,
                    "Greater" => ScheduleFilterType.GreaterThan,
                    "Less" => ScheduleFilterType.LessThan,
                    _ => ScheduleFilterType.Equal
                };
                string value = jf.Value<string>("value");

                var scheduleField = def.GetFieldOrder()
                                       .Select(fid => def.GetField(fid))
                                       .FirstOrDefault(sf =>
                                       {
                                           try { if (!string.IsNullOrWhiteSpace(fieldName) && sf.GetName() == fieldName) return true; } catch { }
                                           try { if (fieldParamId != int.MinValue && sf.ParameterId != null && sf.ParameterId.IntValue() == fieldParamId) return true; } catch { }
                                           return false;
                                       });
                if (scheduleField == null) continue;

                def.AddFilter(new ScheduleFilter(scheduleField.FieldId, filterType, value));
            }

            tx.Commit();
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}


