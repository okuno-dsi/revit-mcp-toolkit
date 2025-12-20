using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class ListSchedulableFieldsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_schedulable_fields";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)(cmd.Params ?? new JObject());
            int scheduleViewId = p.Value<int?>("scheduleViewId") ?? 0;
            string categoryName = p.Value<string>("categoryName");

            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            ViewSchedule vs = null;
            if (scheduleViewId > 0)
            {
                vs = doc.GetElement(new ElementId(scheduleViewId)) as ViewSchedule;
            }
            else if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var cat = doc.Settings.Categories.Cast<Category>()
                    .FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (cat == null) return ResultUtil.Err($"Category '{categoryName}' not found.");
                vs = ViewSchedule.CreateSchedule(doc, cat.Id);
            }
            if (vs == null) return ResultUtil.Err("ScheduleView not found or could not create.");

            var def = vs.Definition;
            var avail = def.GetSchedulableFields();
            var items = new List<object>(avail.Count);
            foreach (var f in avail)
            {
                int pid = -1; try { pid = f.ParameterId?.IntegerValue ?? -1; } catch { pid = -1; }
                string name = ""; try { name = f.GetName(doc); } catch { }
                items.Add(new { name, parameterId = pid, type = f.FieldType.ToString() });
            }

            return new { ok = true, fields = items, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

