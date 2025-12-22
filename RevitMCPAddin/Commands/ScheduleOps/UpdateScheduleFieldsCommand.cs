// RevitMCPAddin/Commands/ScheduleOps/UpdateScheduleFieldsCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class UpdateScheduleFieldsCommand : IRevitCommandHandler
    {
        public string CommandName => "update_schedule_fields";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("scheduleViewId");
            var addNames = p["addFields"]?.ToObject<List<string>>() ?? new List<string>();
            var removeNames = p["removeFields"]?.ToObject<List<string>>() ?? new List<string>();
            var addParamIds = p["addParamIds"]?.ToObject<List<int>>() ?? new List<int>();
            var removeParamIds = p["removeParamIds"]?.ToObject<List<int>>() ?? new List<int>();
            var setHeadings = p["setHeadings"]?.ToObject<List<JObject>>() ?? new List<JObject>(); // [{ name|paramId, heading }]
            bool? isItemized = p.Value<bool?>("isItemized");
            bool? showGrandTotals = p.Value<bool?>("showGrandTotals");
            bool addElementId = p.Value<bool?>("addElementId") ?? false;

            Document doc = uiapp.ActiveUIDocument.Document;
            var vs = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ViewSchedule;
            if (vs == null)
                return new { ok = false, message = $"ScheduleView {id} not found.", units = UnitHelper.DefaultUnitsMeta() };

            using var tx = new Transaction(doc, "Update Schedule Fields");
            tx.Start();

            var def = vs.Definition;
            // 0) Itemize / GrandTotals の設定（指定があれば）
            try { if (isItemized.HasValue) def.IsItemized = isItemized.Value; } catch { }
            if (showGrandTotals.HasValue)
            {
                // Revit API versions differ: some expose ShowGrandTotal(s) on ScheduleDefinition, others don't.
                // Use reflection to avoid compile-time dependency on a specific property name.
                UpdateScheduleFieldsCommand_Compat.TrySetPropertyIfExists(def, "ShowGrandTotals", showGrandTotals.Value);
                UpdateScheduleFieldsCommand_Compat.TrySetPropertyIfExists(def, "ShowGrandTotal", showGrandTotals.Value);
            }

            // 1) 既存フィールドの削除（ScheduleField.GetName() / ParameterId）
            if (removeNames.Count > 0 || removeParamIds.Count > 0)
            {
                foreach (var sfId in def.GetFieldOrder().ToList())
                {
                    var scheduleField = def.GetField(sfId);
                    bool byName = removeNames.Contains(scheduleField.GetName());
                    bool byParam = false;
                    try { if (removeParamIds.Contains(scheduleField.ParameterId.IntValue())) byParam = true; } catch { }
                    if (byName || byParam)
                    {
                        def.RemoveField(sfId);
                    }
                }
            }

            // 2) 追加（SchedulableField.GetName(doc)）
            var available = def.GetSchedulableFields();
            foreach (var name in addNames)
            {
                var sf = available.FirstOrDefault(f => f.GetName(doc).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (sf != null) def.AddField(sf);
            }

            // 2.5) 追加（ParameterId 指定）
            foreach (var pid in addParamIds.Distinct())
            {
                var sf = available.FirstOrDefault(f =>
                {
                    try { return f.ParameterId != null && f.ParameterId.IntValue() == pid; }
                    catch { return false; }
                });
                if (sf != null) def.AddField(sf);
            }

            // 2.6) ElementId 列の追加（可能な場合）
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
                            // 多言語対応: "ID" / "要素 ID" / localized variants that contain ID
                            return string.Equals(nm, "ID", StringComparison.OrdinalIgnoreCase) ||
                                   nm.Contains("要素") && nm.Contains("ID") ||
                                   string.Equals(nm, "要素 ID", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    });
                    if (sfId != null) def.AddField(sfId);
                }
                catch { /* ignore */ }
            }

            // 3) 見出し（ヘッダ）変更
            if (setHeadings.Count > 0)
            {
                var fieldOrder = def.GetFieldOrder().ToList();
                foreach (var obj in setHeadings)
                {
                    string heading = obj.Value<string>("heading");
                    if (string.IsNullOrWhiteSpace(heading)) continue;

                    string nameKey = obj.Value<string>("name");
                    int pidKey = obj.Value<int?>("paramId") ?? int.MinValue;

                    foreach (var fid in fieldOrder)
                    {
                        var sf = def.GetField(fid);
                        bool match = false;
                        try { if (!string.IsNullOrWhiteSpace(nameKey) && sf.GetName() == nameKey) match = true; } catch { }
                        try { if (!match && pidKey != int.MinValue && sf.ParameterId != null && sf.ParameterId.IntValue() == pidKey) match = true; } catch { }
                        if (match)
                        {
                            try { sf.ColumnHeading = heading; } catch { }
                            break;
                        }
                    }
                }
            }

            tx.Commit();
            // 応答に現在の可視列を返しておく（確認用）
            try
            {
                var fields = def.GetFieldOrder().Select(fid =>
                {
                    var f = def.GetField(fid);
                    int pid = -1; try { pid = f.ParameterId?.IntValue() ?? -1; } catch { }
                    string nm = ""; try { nm = f.GetName(); } catch { }
                    string head = ""; try { head = f.ColumnHeading; } catch { }
                    return new { name = nm, paramId = pid, heading = head, hidden = f.IsHidden };
                }).ToList();
                return new { ok = true, fields, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch { return new { ok = true, units = UnitHelper.DefaultUnitsMeta() }; }
        }
    }
}

// local helpers
namespace RevitMCPAddin.Commands.ScheduleOps
{
    internal static class UpdateScheduleFieldsCommand_Compat
    {
        internal static void TrySetPropertyIfExists(object target, string propName, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            var p = t.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(target, value, null); } catch { /* ignore */ }
            }
        }
    }
}


