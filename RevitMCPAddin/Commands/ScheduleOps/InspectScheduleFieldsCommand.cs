// Proposed new command for RevitMCPAddin
// Namespace and patterns align with existing handlers (see GetScheduleDataCommand.cs)
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class InspectScheduleFieldsCommand : IRevitCommandHandler
    {
        public string CommandName => "inspect_schedule_fields";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)cmd.Params;
                int id = p.Value<int?>("scheduleViewId") ?? 0;
                string title = p.Value<string>("title");
                int samplePerField = Math.Max(1, p.Value<int?>("samplePerField") ?? 5);

                var doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                    return ResultUtil.Err("No active document.");

                ViewSchedule vs = null;
                if (id > 0)
                {
                    vs = doc.GetElement(new ElementId(id)) as ViewSchedule;
                }
                else if (!string.IsNullOrWhiteSpace(title))
                {
                    vs = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .FirstOrDefault(x => string.Equals(x.Name, title, StringComparison.Ordinal));
                }

                if (vs == null)
                    return ResultUtil.Err("ScheduleView not found (specify scheduleViewId or title).");

                var table = vs.GetTableData();
                var header = table.GetSectionData(SectionType.Header);
                var body = table.GetSectionData(SectionType.Body);
                if (!IsValidSection(body))
                    return ResultUtil.Err("The schedule Body section has no rows/columns to read.");

                var def = vs.Definition;
                var visibleFields = def.GetFieldOrder()
                    .Select(fid => def.GetField(fid))
                    .Where(sf => sf != null && !sf.IsHidden)
                    .ToList();

                int colStart = body.FirstColumnNumber;
                int colEnd = body.LastColumnNumber;
                int visibleColCount = Math.Min(visibleFields.Count, Math.Max(0, colEnd - colStart + 1));
                if (visibleColCount <= 0)
                    return ResultUtil.Err("No visible columns to read in schedule Body.");

                // Build display name mapping: try header last row first, fallback to ScheduleField.GetName()
                var columns = new List<ColumnInfo>(visibleColCount);
                for (int i = 0; i < visibleColCount; i++)
                {
                    int col = colStart + i;
                    string display = null;
                    if (IsValidSection(header))
                    {
                        try { display = header.GetCellText(header.LastRowNumber, col); } catch { display = null; }
                    }
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        try { display = visibleFields[i].GetName(); } catch { display = $"Column{i + 1}"; }
                    }

                    string internalName;
                    try { internalName = visibleFields[i].GetName(); }
                    catch { internalName = $"Field{i + 1}"; }

                    columns.Add(new ColumnInfo { Index = i, DisplayName = display, FieldName = internalName });
                }

                // Sample values per column from body rows
                var samples = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                int totalRows = body.NumberOfRows;
                for (int r = body.FirstRowNumber; r <= body.LastRowNumber; r++)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var col = colStart + i;
                        string v;
                        try { v = body.GetCellText(r, col) ?? string.Empty; }
                        catch { v = string.Empty; }
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            var key = columns[i].DisplayName;
                            if (!samples.TryGetValue(key, out var set))
                            {
                                set = new HashSet<string>();
                                samples[key] = set;
                            }
                            if (set.Count < samplePerField)
                                set.Add(v);
                        }
                    }
                }

                var fields = columns.Select(c => new
                {
                    displayName = c.DisplayName,
                    fieldName = c.FieldName,
                    samples = samples.TryGetValue(c.DisplayName, out var set)
                        ? set.Take(samplePerField).ToArray()
                        : Array.Empty<string>()
                }).ToArray();

                return new
                {
                    ok = true,
                    scheduleViewId = vs.Id.IntegerValue,
                    title = vs.Name,
                    fields,
                    totalRows
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        private static bool IsValidSection(TableSectionData sec)
        {
            if (sec == null) return false;
            try
            {
                return sec.LastRowNumber >= sec.FirstRowNumber &&
                       sec.LastColumnNumber >= sec.FirstColumnNumber &&
                       sec.NumberOfRows >= 0;
            }
            catch { return false; }
        }

        private class ColumnInfo
        {
            public int Index { get; set; }
            public string DisplayName { get; set; }
            public string FieldName { get; set; }
        }
    }
}

