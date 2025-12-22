// RevitMCPAddin/Commands/ScheduleOps/UpdateScheduleSortingCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class UpdateScheduleSortingCommand : IRevitCommandHandler
    {
        public string CommandName => "update_schedule_sorting";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("scheduleViewId");
            var groupBy = p["groupBy"]?.ToObject<List<string>>() ?? new List<string>();
            var groupByParamIds = p["groupByParamIds"]?.ToObject<List<int>>() ?? new List<int>();
            var sortBy = p["sortBy"]?.ToObject<List<JObject>>() ?? new List<JObject>();

            Document doc = uiapp.ActiveUIDocument.Document;
            var vs = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ViewSchedule;
            if (vs == null)
                return new { ok = false, message = $"ScheduleView {id} not found.", units = UnitHelper.DefaultUnitsMeta() };

            using var tx = new Transaction(doc, "Update Schedule Sorting");
            tx.Start();

            var def = vs.Definition;
            def.ClearSortGroupFields();

            var fieldIds = def.GetFieldOrder();

            // ── グループ化 ──
            foreach (var name in groupBy)
            {
                var fid = fieldIds.FirstOrDefault(fid2 => def.GetField(fid2).GetName() == name);
                if (!fid.Equals(default(ScheduleFieldId)))
                {
                    def.AddSortGroupField(new ScheduleSortGroupField(fid));
                }
            }

            foreach (var pid in groupByParamIds.Distinct())
            {
                var fid = fieldIds.FirstOrDefault(fid2 =>
                {
                    var f = def.GetField(fid2);
                    try { return f.ParameterId != null && f.ParameterId.IntValue() == pid; } catch { return false; }
                });
                if (!fid.Equals(default(ScheduleFieldId)))
                {
                    def.AddSortGroupField(new ScheduleSortGroupField(fid));
                }
            }

            // ── ソート ──
            foreach (var jf in sortBy)
            {
                string name = jf.Value<string>("field");
                int pid = jf.Value<int?>("fieldParamId") ?? int.MinValue;
                bool asc = jf.Value<bool>("ascending");
                var fid = fieldIds.FirstOrDefault(fid2 =>
                {
                    var f = def.GetField(fid2);
                    try { if (!string.IsNullOrWhiteSpace(name) && f.GetName() == name) return true; } catch { }
                    try { if (pid != int.MinValue && f.ParameterId != null && f.ParameterId.IntValue() == pid) return true; } catch { }
                    return false;
                });
                if (fid.Equals(default(ScheduleFieldId))) continue;

                var order = asc ? ScheduleSortOrder.Ascending : ScheduleSortOrder.Descending;
                def.AddSortGroupField(new ScheduleSortGroupField(fid, order));
            }

            tx.Commit();
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}


