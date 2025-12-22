// RevitMCPAddin/Commands/ScheduleOps/CreateScheduleViewCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class CreateScheduleViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_schedule_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            string categoryName = p.Value<string>("categoryName")
                                  ?? throw new ArgumentException("categoryName is required");
            var fieldNames = p.Value<JArray>("fieldNames") ?? new JArray();
            var fieldParamIds = p.Value<JArray>("fieldParamIds") ?? new JArray();
            string title = p.Value<string>("title")
                           ?? throw new ArgumentException("title is required");

            Document doc = uiapp.ActiveUIDocument.Document;
            var category = doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null)
                return new { ok = false, message = $"カテゴリ '{categoryName}' が見つかりません。", units = UnitHelper.DefaultUnitsMeta() };

            using var tx = new Transaction(doc, "Create Schedule View");
            tx.Start();
            var schedule = ViewSchedule.CreateSchedule(doc, category.Id);
            schedule.Name = title;

            var def = schedule.Definition;
            var available = def.GetSchedulableFields();
            foreach (var fn in fieldNames.Select(x => x.Value<string>()))
            {
                var sf = available.FirstOrDefault(f => f.GetName(doc) == fn);
                if (sf != null) def.AddField(sf);
            }
            foreach (var pid in fieldParamIds.Select(x => x.Value<int>()).Distinct())
            {
                var sf = available.FirstOrDefault(f =>
                {
                    try { return f.ParameterId != null && f.ParameterId.IntValue() == pid; }
                    catch { return false; }
                });
                if (sf != null) def.AddField(sf);
            }

            // オプション: Itemized / ElementId 列
            bool? isItemized = p.Value<bool?>("isItemized");
            bool addElementId = p.Value<bool?>("addElementId") ?? false;
            try { if (isItemized.HasValue) def.IsItemized = isItemized.Value; } catch { }
            if (addElementId)
            {
                try
                {
                    var sfId = available.FirstOrDefault(f =>
                    {
                        try
                        {
                            var nm = f.GetName(doc);
                            if (string.IsNullOrEmpty(nm)) return false;
                            return string.Equals(nm, "ID", StringComparison.OrdinalIgnoreCase) ||
                                   nm.Contains("要素") && nm.Contains("ID") ||
                                   string.Equals(nm, "要素 ID", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    });
                    if (sfId != null) def.AddField(sfId);
                }
                catch { }
            }
            tx.Commit();

            return new { ok = true, scheduleViewId = schedule.Id.IntValue(), units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

