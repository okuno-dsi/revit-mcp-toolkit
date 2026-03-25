#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Failures;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    internal sealed class ScheduleRoundtripColumn
    {
        public int OutputColumnNumber { get; set; }
        public int ScheduleColumnIndex { get; set; }
        public string Header { get; set; } = string.Empty;
        public string ParamName { get; set; } = string.Empty;
        public int? ParamId { get; set; }
        public string StorageType { get; set; } = string.Empty;
        public string DataTypeId { get; set; } = string.Empty;
        public bool Editable { get; set; }
        public bool IsBoolean { get; set; }
        public string SourceFieldName { get; set; } = string.Empty;
    }

    internal static class ScheduleRoundtripExcelUtil
    {
        public const string ExportModeRoundtrip = "roundtrip";
        public const string ExportModeDisplay = "display";
        public const string ExportModeAuto = "auto";
        public const string HiddenIdHeader = "__ElementId";
        public const string MetaSheetName = "__revit_roundtrip_meta";
        public const string RowMapSheetName = "__revit_roundtrip_rows";
        public const string DataSheetName = "Schedule";
        public const string ReadmeSheetName = "README";
        public const double MaxDataColumnWidth = 20d;
        public const double BooleanColumnWidth = 4.2d;
        private static readonly string[] ElementIdLikeNames =
        {
            "ID",
            "Element ID",
            "要素 ID",
            "要素ID",
            "ID を交換",
            "図形 ID を交換",
            "Exchange ID",
            "Exchange Graphic Id"
        };

        private static readonly int[] ElementIdLikeParameterIds =
        {
            -1155400,
            -1155401
        };

        public static ViewSchedule? ResolveSchedule(UIApplication uiapp, Document doc, JObject p)
        {
            var viewId = p.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as ViewSchedule;

            var viewName = p.Value<string>("viewName");
            if (!string.IsNullOrWhiteSpace(viewName))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(v => !v.IsTemplate &&
                                         string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
            }

            var activeDoc = uiapp.ActiveUIDocument?.Document;
            if (activeDoc != null &&
                string.Equals(activeDoc.PathName ?? string.Empty, doc.PathName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return activeDoc.ActiveView as ViewSchedule;
            }

            return null;
        }

        public static string GetDefaultExportPath(ViewSchedule vs)
        {
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(Path.GetTempPath(), $"ScheduleRoundtrip_{vs.Id.IntValue()}_{ts}.xlsx");
        }

        public static string GetDefaultImportReportPath(string excelPath)
        {
            var dir = Path.GetDirectoryName(excelPath) ?? Path.GetTempPath();
            var name = Path.GetFileNameWithoutExtension(excelPath);
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(dir, $"{name}_import_report_{ts}.csv");
        }

        public static void CleanupTempSchedule(Document doc, ElementId tempId)
        {
            if (tempId == ElementId.InvalidElementId) return;
            using (var tx = new Transaction(doc, "Schedule Roundtrip - cleanup"))
            {
                tx.Start();
                try { doc.Delete(tempId); } catch { }
                tx.Commit();
            }
        }

        public static ViewSchedule PrepareTemporarySchedule(Document doc, ViewSchedule source, out ElementId tempId, bool removeSortGroupFields = true)
        {
            tempId = ElementId.InvalidElementId;
            using (var tx = new Transaction(doc, "Schedule Roundtrip - duplicate"))
            {
                tx.Start();
                tempId = source.Duplicate(ViewDuplicateOption.Duplicate);
                var work = doc.GetElement(tempId) as ViewSchedule;
                if (work == null)
                    throw new InvalidOperationException("Failed to duplicate schedule.");

                var def = work.Definition;
                TrySetPropertyIfExists(def, "IsItemized", true);
                TrySetPropertyIfExists(def, "ShowGrandTotal", false);
                TrySetPropertyIfExists(def, "ShowGrandTotals", false);

                try
                {
                    if (removeSortGroupFields)
                    {
                        while (def.GetSortGroupFieldCount() > 0)
                            def.RemoveSortGroupField(0);
                    }
                }
                catch { }

                EnsureElementIdField(doc, work);
                doc.Regenerate();
                tx.Commit();
                return work;
            }
        }

        public static string DetermineEffectiveExportMode(ViewSchedule schedule)
        {
            if (schedule == null) return ExportModeRoundtrip;

            try
            {
                var def = schedule.Definition;
                bool isItemized = true;
                bool showGrandTotals = false;
                int sortGroupCount = 0;

                try
                {
                    var prop = def.GetType().GetProperty("IsItemized", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                        isItemized = Convert.ToBoolean(prop.GetValue(def, null), CultureInfo.InvariantCulture);
                }
                catch { }

                try
                {
                    sortGroupCount = def.GetSortGroupFieldCount();
                }
                catch { }

                try
                {
                    var prop = def.GetType().GetProperty("ShowGrandTotal", BindingFlags.Public | BindingFlags.Instance)
                               ?? def.GetType().GetProperty("ShowGrandTotals", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                        showGrandTotals = Convert.ToBoolean(prop.GetValue(def, null), CultureInfo.InvariantCulture);
                }
                catch { }

                if (!isItemized) return ExportModeDisplay;
                if (sortGroupCount > 0) return ExportModeDisplay;
                if (showGrandTotals) return ExportModeDisplay;
            }
            catch { }

            return ExportModeRoundtrip;
        }

        public static IList<int> ReadRowElementIds(Document doc, ViewSchedule vs)
        {
            var ids = new List<int>();
            var table = vs.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows <= 0 || body.NumberOfColumns <= 0)
                return ids;

            int elementIdCol = FindElementIdColumnIndex(vs);
            if (elementIdCol < 0) return ids;

            for (int r = 0; r < body.NumberOfRows; r++)
            {
                var text = GetCellText(vs, body, SectionType.Body, r, elementIdCol);
                if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    continue;
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) == null)
                    continue;
                ids.Add(eid);
            }
            return ids;
        }

        public static IList<int> GetElementsInScheduleView(Document doc, ViewSchedule vs)
        {
            try
            {
                return new FilteredElementCollector(doc, vs.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .Select(id => id.IntValue())
                    .Distinct()
                    .ToList();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        public static int FindElementIdColumnIndex(ViewSchedule vs)
        {
            var def = vs.Definition;
            var visibleFields = def.GetFieldOrder()
                .Select(fid => def.GetField(fid))
                .Where(f => f != null && !f.IsHidden)
                .ToList();

            for (int i = 0; i < visibleFields.Count; i++)
            {
                if (IsElementIdLikeField(visibleFields[i]))
                    return i;
            }

            return -1;
        }

        public static string GetFieldHeader(ScheduleField field)
        {
            string? header = null;
            try { header = field.ColumnHeading; } catch { }
            if (!string.IsNullOrWhiteSpace(header)) return header!;
            try { header = field.GetName(); } catch { }
            return header ?? string.Empty;
        }

        public static bool IsElementIdLikeField(ScheduleField? field)
        {
            if (field == null) return false;

            var header = GetFieldHeader(field);
            if (string.Equals(header, HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var candidate in ElementIdLikeNames)
            {
                if (string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            try
            {
                var sourceName = field.GetName() ?? string.Empty;
                foreach (var candidate in ElementIdLikeNames)
                {
                    if (string.Equals(sourceName, candidate, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            try
            {
                var pid = field.ParameterId?.IntValue();
                if (pid.HasValue && ElementIdLikeParameterIds.Contains(pid.Value))
                    return true;
            }
            catch { }

            return false;
        }

        public static bool IsElementIdLikeSchedulableField(Document doc, SchedulableField sf)
        {
            if (sf == null) return false;

            try
            {
                var name = sf.GetName(doc) ?? string.Empty;
                foreach (var candidate in ElementIdLikeNames)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            try
            {
                var pid = sf.ParameterId?.IntValue();
                if (pid.HasValue && ElementIdLikeParameterIds.Contains(pid.Value))
                    return true;
            }
            catch { }

            return false;
        }

        public static string GetCellText(ViewSchedule vs, TableSectionData body, SectionType sectionType, int rowRel, int colRel)
        {
            try { return vs.GetCellText(sectionType, rowRel, colRel) ?? string.Empty; }
            catch
            {
                try { return body.GetCellText(body.FirstRowNumber + rowRel, body.FirstColumnNumber + colRel) ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public static string NormalizeKeyText(string text)
        {
            return string.Join(" ", (text ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();
        }

        public static bool IsYesNoParameter(Parameter prm)
        {
            if (prm == null || prm.StorageType != StorageType.Integer) return false;
            try
            {
                var dt = prm.Definition?.GetDataType();
                if (dt == null) return false;
                var yesNo = GetYesNoSpecTypeId();
                if (yesNo != null && dt.Equals(yesNo)) return true;
                var tid = dt.TypeId ?? string.Empty;
                return tid.IndexOf("yesno", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static ForgeTypeId? GetYesNoSpecTypeId()
        {
            try
            {
                var nested = typeof(SpecTypeId).GetNestedType("Boolean", BindingFlags.Public);
                var prop = nested?.GetProperty("YesNo", BindingFlags.Public | BindingFlags.Static);
                return prop?.GetValue(null, null) as ForgeTypeId;
            }
            catch { return null; }
        }

        public static string TryGetDataTypeId(Definition? def)
        {
            try { return def?.GetDataType()?.TypeId ?? string.Empty; } catch { return string.Empty; }
        }

        private static JObject BuildResolverPayload(ScheduleRoundtripColumn col)
        {
            var payload = new JObject();
            if (col.ParamId.HasValue) payload["paramId"] = col.ParamId.Value;
            if (!string.IsNullOrWhiteSpace(col.ParamName)) payload["paramName"] = col.ParamName;
            if (!string.IsNullOrWhiteSpace(col.SourceFieldName) &&
                !string.Equals(col.SourceFieldName, col.ParamName, StringComparison.OrdinalIgnoreCase))
            {
                payload["name"] = col.SourceFieldName;
            }
            return payload;
        }

        public static Parameter? ResolveParameterOnElementOrType(Element? element, ScheduleRoundtripColumn col, out Element? owner, out string scope)
        {
            owner = element;
            scope = "none";
            if (element == null) return null;

            var payload = BuildResolverPayload(col);

            var prm = ParamResolver.ResolveByPayload(element, payload, out _);
            if (prm != null)
            {
                owner = element;
                scope = "instance";
                return prm;
            }

            try
            {
                var typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeElem = element.Document.GetElement(typeId);
                    if (typeElem != null)
                    {
                        prm = ParamResolver.ResolveByPayload(typeElem, payload, out _);
                        if (prm != null)
                        {
                            owner = typeElem;
                            scope = "type";
                            return prm;
                        }
                    }
                }
            }
            catch { }

            owner = element;
            return null;
        }

        public static IList<ScheduleRoundtripColumn> BuildColumns(Document doc, ViewSchedule vs, IList<int> rowElementIds)
        {
            var def = vs.Definition;
            var visibleFields = def.GetFieldOrder()
                .Select(fid => def.GetField(fid))
                .Where(f => f != null && !f.IsHidden)
                .ToList();

            var rowElements = rowElementIds
                .Select(id => doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)))
                .Where(e => e != null)
                .ToList();

            var cols = new List<ScheduleRoundtripColumn>();
            int outputCol = 2;
            int scheduleCol = 0;

            foreach (var field in visibleFields)
            {
                var header = GetFieldHeader(field);
                if (string.Equals(header, HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                {
                    scheduleCol++;
                    continue;
                }

                string sourceFieldName = string.Empty;
                try { sourceFieldName = field.GetName() ?? string.Empty; } catch { }

                int? paramId = null;
                try { paramId = field.ParameterId?.IntValue(); } catch { }

                var probeCol = new ScheduleRoundtripColumn
                {
                    ParamId = paramId,
                    ParamName = sourceFieldName,
                    SourceFieldName = sourceFieldName
                };

                Parameter? prm = null;
                bool editable = false;
                bool isBoolean = false;

                foreach (var rowElement in rowElements)
                {
                    var candidate = ResolveParameterOnElementOrType(rowElement, probeCol, out _, out _);
                    if (candidate == null)
                        continue;

                    prm ??= candidate;

                    if (IsYesNoParameter(candidate))
                        isBoolean = true;

                    if (!candidate.IsReadOnly && candidate.StorageType != StorageType.ElementId)
                    {
                        editable = true;
                        prm = candidate;
                        break;
                    }
                }

                if (!isBoolean && IsBooleanLikeDisplayColumn(vs, scheduleCol))
                    isBoolean = true;

                cols.Add(new ScheduleRoundtripColumn
                {
                    OutputColumnNumber = outputCol++,
                    ScheduleColumnIndex = scheduleCol++,
                    Header = header,
                    ParamName = sourceFieldName,
                    ParamId = paramId,
                    Editable = editable,
                    IsBoolean = isBoolean,
                    StorageType = prm?.StorageType.ToString() ?? string.Empty,
                    DataTypeId = TryGetDataTypeId(prm?.Definition),
                    SourceFieldName = sourceFieldName
                });
            }

            return cols;
        }

        public static bool IsBooleanLikeDisplayColumn(ViewSchedule vs, int scheduleColumnIndex)
        {
            try
            {
                var table = vs.GetTableData();
                var body = table.GetSectionData(SectionType.Body);
                if (body == null || body.NumberOfRows <= 0)
                    return false;

                int nonEmptyCount = 0;
                for (int r = 0; r < body.NumberOfRows; r++)
                {
                    var text = (GetCellText(vs, body, SectionType.Body, r, scheduleColumnIndex) ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    nonEmptyCount++;
                    if (!TryParseDisplayedBooleanText(text, out _))
                        return false;
                }

                return nonEmptyCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public static IList<string> ReadDisplayRowValues(ViewSchedule vs, IList<ScheduleRoundtripColumn> cols, int rowRel)
        {
            var values = new List<string>();
            var table = vs.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null) return values;

            foreach (var col in cols)
            {
                values.Add(GetCellText(vs, body, SectionType.Body, rowRel, col.ScheduleColumnIndex));
            }

            return values;
        }

        public static string BuildRowKeyFromValues(IList<string> values, IList<int> keyIndexes)
        {
            if (keyIndexes == null || keyIndexes.Count == 0)
                keyIndexes = Enumerable.Range(0, values.Count).ToList();

            return string.Join("||", keyIndexes
                .Where(i => i >= 0 && i < values.Count)
                .Select(i => NormalizeKeyText(values[i])));
        }

        public static IList<int> GetDisplayKeyColumnIndexes(ViewSchedule vs, IList<ScheduleRoundtripColumn> cols)
        {
            var indexes = new List<int>();
            var def = vs.Definition;
            var visibleFields = def.GetFieldOrder()
                .Select(fid => def.GetField(fid))
                .Where(f => f != null && !f.IsHidden)
                .ToList();

            for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
            {
                try
                {
                    var sg = def.GetSortGroupField(i);
                    var sgFieldId = sg.GetType().GetProperty("FieldId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(sg, null);
                    if (sgFieldId == null) continue;

                    for (int colIdx = 0; colIdx < cols.Count; colIdx++)
                    {
                        var scheduleColIndex = cols[colIdx].ScheduleColumnIndex;
                        if (scheduleColIndex < 0 || scheduleColIndex >= visibleFields.Count) continue;

                        var visibleField = visibleFields[scheduleColIndex];
                        var visibleFieldId = visibleField?.GetType().GetProperty("FieldId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(visibleField, null);
                        if (visibleFieldId == null) continue;

                        if (string.Equals(visibleFieldId.ToString(), sgFieldId.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            indexes.Add(colIdx);
                            break;
                        }
                    }
                }
                catch { }
            }

            if (indexes.Count > 0)
                return indexes;

            return Enumerable.Range(0, cols.Count).ToList();
        }

        public static IDictionary<string, List<int>> BuildDisplayRowElementMap(Document doc, ViewSchedule source, ViewSchedule mappingWork, IList<ScheduleRoundtripColumn> cols)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var keyIndexes = GetDisplayKeyColumnIndexes(source, cols);
            var workTable = mappingWork.GetTableData();
            var workBody = workTable.GetSectionData(SectionType.Body);
            if (workBody == null) return map;

            int elementIdCol = FindElementIdColumnIndex(mappingWork);
            if (elementIdCol < 0) return map;

            for (int r = 0; r < workBody.NumberOfRows; r++)
            {
                var idText = GetCellText(mappingWork, workBody, SectionType.Body, r, elementIdCol);
                if (!int.TryParse((idText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    continue;
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) == null)
                    continue;

                var rowValues = new List<string>();
                foreach (var col in cols)
                    rowValues.Add(GetCellText(mappingWork, workBody, SectionType.Body, r, col.ScheduleColumnIndex));

                var key = BuildRowKeyFromValues(rowValues, keyIndexes);
                if (!map.TryGetValue(key, out var ids))
                {
                    ids = new List<int>();
                    map[key] = ids;
                }

                if (!ids.Contains(eid))
                    ids.Add(eid);
            }

            return map;
        }

        public static IDictionary<string, List<int>> BuildExactDisplayRowElementMap(Document doc, ViewSchedule mappingWork, IList<ScheduleRoundtripColumn> cols)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var workTable = mappingWork.GetTableData();
            var workBody = workTable.GetSectionData(SectionType.Body);
            if (workBody == null) return map;

            int elementIdCol = FindElementIdColumnIndex(mappingWork);
            if (elementIdCol < 0) return map;

            for (int r = 0; r < workBody.NumberOfRows; r++)
            {
                var idText = GetCellText(mappingWork, workBody, SectionType.Body, r, elementIdCol);
                if (!int.TryParse((idText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    continue;
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) == null)
                    continue;

                var rowValues = new List<string>();
                foreach (var col in cols)
                    rowValues.Add(GetCellText(mappingWork, workBody, SectionType.Body, r, col.ScheduleColumnIndex));

                var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                if (!map.TryGetValue(key, out var ids))
                {
                    ids = new List<int>();
                    map[key] = ids;
                }

                if (!ids.Contains(eid))
                    ids.Add(eid);
            }

            return map;
        }

        public static IList<(int ElementId, IList<string> Values)> BuildItemizedDisplayRows(Document doc, ViewSchedule mappingWork, IList<ScheduleRoundtripColumn> cols)
        {
            var rows = new List<(int ElementId, IList<string> Values)>();
            var workTable = mappingWork.GetTableData();
            var workBody = workTable.GetSectionData(SectionType.Body);
            if (workBody == null) return rows;

            int elementIdCol = FindElementIdColumnIndex(mappingWork);
            if (elementIdCol < 0) return rows;

            for (int r = 0; r < workBody.NumberOfRows; r++)
            {
                var idText = GetCellText(mappingWork, workBody, SectionType.Body, r, elementIdCol);
                if (!int.TryParse((idText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    continue;
                if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) == null)
                    continue;

                rows.Add((eid, ReadDisplayRowValues(mappingWork, cols, r)));
            }

            return rows;
        }

        public static IDictionary<string, List<int>> BuildIdentityValueElementMap(
            Document doc,
            IList<int> rowElementIds,
            IList<ScheduleRoundtripColumn> cols)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            if (rowElementIds == null || cols == null || cols.Count == 0)
                return map;

            for (int colIndex = 0; colIndex < cols.Count; colIndex++)
            {
                var col = cols[colIndex];
                if (!IsIdentityLikeHeader(col.Header))
                    continue;

                foreach (var eid in rowElementIds.Distinct())
                {
                    var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                    if (element == null)
                        continue;

                    var value = GetExportValueForColumn(element, col, string.Empty);
                    if (IsDisplayPlaceholderValue(value, col.Header))
                        continue;

                    var key = BuildIdentityMapKey(colIndex, value);
                    if (!map.TryGetValue(key, out var ids))
                    {
                        ids = new List<int>();
                        map[key] = ids;
                    }

                    if (!ids.Contains(eid))
                        ids.Add(eid);
                }
            }

            return map;
        }

        public static bool IsDisplayPlaceholderValue(string? text, string? header = null)
        {
            var normalized = NormalizeKeyText(text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized)) return true;

            if (!string.IsNullOrWhiteSpace(header) &&
                string.Equals(normalized, NormalizeKeyText(header), StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith("<", StringComparison.OrdinalIgnoreCase) &&
                normalized.EndsWith(">", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool IsIdentityLikeHeader(string? header)
        {
            var normalized = NormalizeKeyText(header ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized)) return false;

            return normalized.IndexOf("タイプ", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.IndexOf("符号", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.IndexOf("番号", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static IList<int> SelectiveMatchDisplayRowToElementIds(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IList<(int ElementId, IList<string> Values)> itemizedRows)
        {
            var selectedIndexes = new List<int>();
            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                if (IsDisplayPlaceholderValue(displayRowValues[i], cols[i].Header))
                    continue;
                selectedIndexes.Add(i);
            }

            if (selectedIndexes.Count == 0)
                return Array.Empty<int>();

            var identityIndexes = selectedIndexes
                .Where(i => IsIdentityLikeHeader(cols[i].Header))
                .ToList();
            var identityMatches = MatchDisplayRowToElementIdsByIndexes(displayRowValues, itemizedRows, identityIndexes);
            if (identityMatches.Count > 0)
                return identityMatches;

            var nonEditableIndexes = selectedIndexes
                .Where(i => !cols[i].Editable)
                .ToList();
            var nonEditableMatches = MatchDisplayRowToElementIdsByIndexes(displayRowValues, itemizedRows, nonEditableIndexes);
            if (nonEditableMatches.Count > 0)
                return nonEditableMatches;

            if (selectedIndexes.Count == 1 && !IsIdentityLikeHeader(cols[selectedIndexes[0]].Header))
                return Array.Empty<int>();

            return MatchDisplayRowToElementIdsByIndexes(displayRowValues, itemizedRows, selectedIndexes);
        }

        public static IList<int> MatchDisplayRowToElementIdsByIdentityValueMap(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IDictionary<string, List<int>> identityValueElementMap)
        {
            if (displayRowValues == null || cols == null || identityValueElementMap == null)
                return Array.Empty<int>();

            HashSet<int>? matched = null;
            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                if (!IsIdentityLikeHeader(cols[i].Header))
                    continue;
                if (IsDisplayPlaceholderValue(displayRowValues[i], cols[i].Header))
                    continue;

                if (!identityValueElementMap.TryGetValue(BuildIdentityMapKey(i, displayRowValues[i]), out var ids) || ids.Count == 0)
                    continue;

                if (matched == null)
                {
                    matched = new HashSet<int>(ids);
                }
                else
                {
                    matched.IntersectWith(ids);
                }
            }

            if (matched == null || matched.Count == 0)
                return Array.Empty<int>();

            return matched.OrderBy(x => x).ToList();
        }

        private static IList<int> MatchDisplayRowToElementIdsByIndexes(
            IList<string> displayRowValues,
            IList<(int ElementId, IList<string> Values)> itemizedRows,
            IList<int> selectedIndexes)
        {
            if (selectedIndexes == null || selectedIndexes.Count == 0)
                return Array.Empty<int>();

            var matched = new List<int>();
            foreach (var row in itemizedRows)
            {
                bool allMatched = true;
                foreach (var idx in selectedIndexes)
                {
                    var displayValue = NormalizeKeyText(displayRowValues[idx]);
                    var itemizedValue = idx < row.Values.Count ? NormalizeKeyText(row.Values[idx]) : string.Empty;
                    if (!string.Equals(displayValue, itemizedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        allMatched = false;
                        break;
                    }
                }

                if (allMatched && !matched.Contains(row.ElementId))
                    matched.Add(row.ElementId);
            }

            return matched;
        }

        private static string BuildIdentityMapKey(int colIndex, string value)
        {
            return $"{colIndex}:{NormalizeKeyText(value ?? string.Empty)}";
        }

        public static bool HasOnlyBlankNonKeyValues(IList<string> values, IList<int> keyIndexes)
        {
            var keySet = new HashSet<int>(keyIndexes ?? Array.Empty<int>());
            for (int i = 0; i < values.Count; i++)
            {
                if (keySet.Contains(i)) continue;
                if (!string.IsNullOrWhiteSpace(NormalizeKeyText(values[i])))
                    return false;
            }

            return true;
        }

        public static IList<int> ParseElementIds(string text)
        {
            var ids = new List<int>();
            var tokens = (text ?? string.Empty)
                .Split(new[] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (!int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    continue;
                if (!ids.Contains(eid))
                    ids.Add(eid);
            }

            return ids;
        }

        public static IDictionary<string, int> BuildWorksheetHeaderColumnMap(IXLWorksheet ws)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                var header = NormalizeKeyText(ws.Cell(1, col).GetString());
                if (string.IsNullOrWhiteSpace(header)) continue;
                if (!map.ContainsKey(header))
                    map[header] = col;
            }

            return map;
        }

        public static IList<string> ReadWorksheetDisplayRowValues(
            IXLWorksheet ws,
            int rowNumber,
            IList<ScheduleRoundtripColumn> cols,
            IDictionary<string, int> headerColumnMap)
        {
            var values = new List<string>();
            foreach (var col in cols)
            {
                var normalizedHeader = NormalizeKeyText(col.Header);
                if (!headerColumnMap.TryGetValue(normalizedHeader, out var colNumber))
                {
                    values.Add(string.Empty);
                    continue;
                }

                values.Add(ws.Cell(rowNumber, colNumber).GetString());
            }

            return values;
        }

        public static string GetExportValueForColumn(Element element, ScheduleRoundtripColumn col, string displayText)
        {
            if (col.IsBoolean)
            {
                if (element != null)
                {
                    var booleanParam = ResolveParameterOnElementOrType(element, col, out _, out _);
                    if (booleanParam != null)
                        return booleanParam.AsInteger() != 0 ? "☑" : "☐";
                }

                if (TryParseDisplayedBooleanText(displayText, out var displayBool))
                    return displayBool ? "☑" : "☐";
            }

            if (element == null)
                return displayText ?? string.Empty;

            var prm = ResolveParameterOnElementOrType(element, col, out _, out _);
            if (prm == null) return displayText ?? string.Empty;

            try
            {
                switch (prm.StorageType)
                {
                    case StorageType.String:
                        return prm.AsString() ?? string.Empty;
                    case StorageType.Integer:
                        return prm.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        return prm.AsValueString() ?? displayText ?? string.Empty;
                    case StorageType.ElementId:
                        if (!string.IsNullOrWhiteSpace(displayText))
                            return displayText ?? string.Empty;

                        var refId = prm.AsElementId();
                        if (refId == null || refId == ElementId.InvalidElementId)
                            return string.Empty;

                        var refElem = element.Document.GetElement(refId);
                        if (refElem != null)
                        {
                            try
                            {
                                var named = refElem.Name;
                                if (!string.IsNullOrWhiteSpace(named))
                                    return named;
                            }
                            catch { }
                        }

                        return (refId.IntValue()).ToString(CultureInfo.InvariantCulture);
                    default:
                        return displayText ?? string.Empty;
                }
            }
            catch
            {
                return displayText ?? string.Empty;
            }
        }

        public static void WriteMetaSheet(IXLWorksheet meta, Document doc, ViewSchedule schedule, IList<ScheduleRoundtripColumn> cols)
        {
            meta.Cell(1, 1).Value = "docTitle";
            meta.Cell(1, 2).Value = doc.Title;
            meta.Cell(2, 1).Value = "scheduleViewId";
            meta.Cell(2, 2).Value = schedule.Id.IntValue();
            meta.Cell(3, 1).Value = "scheduleName";
            meta.Cell(3, 2).Value = schedule.Name;
            meta.Cell(4, 1).Value = "exportMode";
            meta.Cell(4, 2).Value = ExportModeRoundtrip;

            meta.Cell(5, 1).Value = "outputColumn";
            meta.Cell(5, 2).Value = "header";
            meta.Cell(5, 3).Value = "paramName";
            meta.Cell(5, 4).Value = "paramId";
            meta.Cell(5, 5).Value = "storageType";
            meta.Cell(5, 6).Value = "dataTypeId";
            meta.Cell(5, 7).Value = "editable";
            meta.Cell(5, 8).Value = "isBoolean";
            meta.Cell(5, 9).Value = "sourceFieldName";

            int row = 6;
            foreach (var col in cols)
            {
                meta.Cell(row, 1).Value = col.OutputColumnNumber;
                meta.Cell(row, 2).Value = col.Header;
                meta.Cell(row, 3).Value = col.ParamName;
                meta.Cell(row, 4).Value = col.ParamId.HasValue ? col.ParamId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                meta.Cell(row, 5).Value = col.StorageType;
                meta.Cell(row, 6).Value = col.DataTypeId;
                meta.Cell(row, 7).Value = col.Editable ? "true" : "false";
                meta.Cell(row, 8).Value = col.IsBoolean ? "true" : "false";
                meta.Cell(row, 9).Value = col.SourceFieldName;
                row++;
            }

            meta.Columns().AdjustToContents();
            meta.Hide();
        }

        public static void WriteRowMapSheet(IXLWorksheet rowMapSheet, IDictionary<int, List<int>> rowMap, string exportMode)
        {
            rowMapSheet.Cell(1, 1).Value = "exportMode";
            rowMapSheet.Cell(1, 2).Value = exportMode;
            rowMapSheet.Cell(3, 1).Value = "row";
            rowMapSheet.Cell(3, 2).Value = "elementIds";

            int row = 4;
            foreach (var kv in rowMap.OrderBy(x => x.Key))
            {
                rowMapSheet.Cell(row, 1).Value = kv.Key;
                rowMapSheet.Cell(row, 2).Value = string.Join(";", kv.Value ?? new List<int>());
                row++;
            }

            rowMapSheet.Columns().AdjustToContents();
            rowMapSheet.Hide();
        }

        public static IDictionary<int, List<int>> ReadRowMapSheet(IXLWorksheet? rowMapSheet)
        {
            var map = new Dictionary<int, List<int>>();
            if (rowMapSheet == null) return map;

            int row = 4;
            while (!rowMapSheet.Cell(row, 1).IsEmpty())
            {
                var rowNoText = rowMapSheet.Cell(row, 1).GetString();
                if (!int.TryParse(rowNoText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNo) || rowNo <= 0)
                {
                    row++;
                    continue;
                }

                var ids = ParseElementIds(rowMapSheet.Cell(row, 2).GetString()).ToList();
                if (ids.Count > 0)
                    map[rowNo] = ids;

                row++;
            }

            return map;
        }

        public static IList<ScheduleRoundtripColumn> ReadMetaSheet(IXLWorksheet meta)
        {
            var cols = new List<ScheduleRoundtripColumn>();
            int row = 6;
            while (!meta.Cell(row, 1).IsEmpty())
            {
                cols.Add(new ScheduleRoundtripColumn
                {
                    OutputColumnNumber = SafeInt(meta.Cell(row, 1).GetString()),
                    Header = meta.Cell(row, 2).GetString(),
                    ParamName = meta.Cell(row, 3).GetString(),
                    ParamId = TryParseNullableInt(meta.Cell(row, 4).GetString()),
                    StorageType = meta.Cell(row, 5).GetString(),
                    DataTypeId = meta.Cell(row, 6).GetString(),
                    Editable = ParseBool(meta.Cell(row, 7).GetString()),
                    IsBoolean = ParseBool(meta.Cell(row, 8).GetString()),
                    SourceFieldName = meta.Cell(row, 9).GetString()
                });
                row++;
            }
            return cols;
        }

        public static void WriteReadmeSheet(IXLWorksheet ws)
        {
            ws.Cell(1, 1).Value = "Revit Schedule Roundtrip";
            ws.Cell(2, 1).Value = "1. 表示シートで値を編集してください。";
            ws.Cell(3, 1).Value = "2. Yes/No 列は ☑ / ☐ で入力できます。";
            ws.Cell(4, 1).Value = "3. A列(__ElementId) は hidden です。display モードでは 1行に複数要素IDが入ることがあります。";
            ws.Cell(5, 1).Value = "4. 灰色列は読み取り専用で、Revit へ戻しても更新されません。";
            ws.Cell(6, 1).Value = "5. 編集後は import_schedule_roundtrip_excel で Revit に反映します。";
            ws.Column(1).Width = 100;
        }

        public static void ApplyColumnStyles(IXLWorksheet ws, IList<ScheduleRoundtripColumn> cols, int lastRow)
        {
            foreach (var col in cols)
            {
                var rng = ws.Range(2, col.OutputColumnNumber, Math.Max(2, lastRow), col.OutputColumnNumber);
                if (col.IsBoolean)
                {
                    rng.Style.Fill.BackgroundColor = XLColor.LightGreen;
                    rng.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    for (int row = 2; row <= Math.Max(2, lastRow); row++)
                    {
                        try
                        {
                            var dv = ws.Cell(row, col.OutputColumnNumber).DataValidation;
                            dv.IgnoreBlanks = true;
                            dv.InCellDropdown = true;
                            dv.InputTitle = "☑ / ☐";
                            dv.InputMessage = "☑ または ☐ を入力してください。";
                            dv.ErrorTitle = "入力エラー";
                            dv.ErrorMessage = "☑ または ☐ を入力してください。";
                            dv.ShowInputMessage = true;
                            dv.ShowErrorMessage = true;
                            dv.List("\"☑,☐\"", true);
                        }
                        catch { }
                    }
                }
                else if (col.Editable)
                {
                    rng.Style.Fill.BackgroundColor = XLColor.LightYellow;
                }
                else
                {
                    rng.Style.Fill.BackgroundColor = XLColor.LightGray;
                }
            }
        }

        public static void ApplyDataColumnWidths(IXLWorksheet ws, IList<ScheduleRoundtripColumn> cols, int lastRow, bool autoFit)
        {
            foreach (var col in cols)
            {
                var xlCol = ws.Column(col.OutputColumnNumber);

                if (autoFit)
                {
                    try { xlCol.AdjustToContents(1, Math.Max(2, lastRow)); } catch { }
                }

                if (col.IsBoolean)
                {
                    xlCol.Width = BooleanColumnWidth;
                }
                else if (xlCol.Width > MaxDataColumnWidth)
                {
                    xlCol.Width = MaxDataColumnWidth;
                }
            }
        }

        public static bool TryApplyImportedValue(Parameter param, ScheduleRoundtripColumn col, string rawText, out string message)
        {
            message = string.Empty;
            var text = (rawText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                message = "blank-skip";
                return false;
            }

            try
            {
                if (col.IsBoolean || IsYesNoParameter(param))
                {
                    if (!TryParseBooleanText(text, out var b))
                    {
                        message = "Expected ☑/☐.";
                        return false;
                    }
                    return param.Set(b ? 1 : 0);
                }

                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.Set(text);
                    case StorageType.Integer:
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                            return param.Set(iv);
                        try { if (param.SetValueString(text)) return true; } catch { }
                        message = "Expected integer.";
                        return false;
                    case StorageType.Double:
                        try { if (param.SetValueString(text)) return true; } catch { }
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                            return UnitHelper.TrySetParameterByExternalValue(param, dv, out message);
                        message = "Expected numeric/project-unit text.";
                        return false;
                    case StorageType.ElementId:
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                            return param.Set(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                        message = "Expected ElementId integer.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }

            message = "Unsupported StorageType.";
            return false;
        }

        public static bool TryParseBooleanText(string text, out bool value)
        {
            var s = (text ?? string.Empty).Trim();
            if (string.Equals(s, "☑", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "ON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "YES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "はい", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "オン", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "有", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "有効", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(s, "☐", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "OFF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "いいえ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "オフ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "無", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "無効", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        public static bool TryParseDisplayedBooleanText(string text, out bool value)
        {
            var s = (text ?? string.Empty).Trim();
            if (string.Equals(s, "☑", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "ON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "YES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "はい", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "オン", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "有", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "有効", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(s, "☐", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "OFF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "NO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "いいえ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "オフ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "無", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "無効", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        public static void EnsureElementIdField(Document doc, ViewSchedule vs)
        {
            var def = vs.Definition;
            var available = def.GetSchedulableFields();
            SchedulableField? target = null;
            foreach (var sf in available)
            {
                if (IsElementIdLikeSchedulableField(doc, sf))
                {
                    target = sf;
                    break;
                }
            }

            if (target == null) return;

            ScheduleField? existing = null;
            foreach (var fid in def.GetFieldOrder())
            {
                var f = def.GetField(fid);
                try
                {
                    if (target != null && f.ParameterId != null && target.ParameterId != null &&
                        f.ParameterId.IntValue() == target.ParameterId.IntValue())
                    {
                        existing = f;
                        break;
                    }
                }
                catch { }

                if (existing == null && IsElementIdLikeField(f))
                {
                    existing = f;
                    break;
                }
            }

            if (existing == null)
            {
                try
                {
                    existing = def.AddField(target);
                }
                catch { }
            }

            if (existing != null)
            {
                try { existing.IsHidden = false; } catch { }
                try { existing.ColumnHeading = HiddenIdHeader; } catch { }
            }
        }

        private static void TrySetPropertyIfExists(object target, string propName, object? value)
        {
            if (target == null) return;
            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(target, value, null); } catch { }
            }
        }

        private static bool ParseBool(string s)
        {
            return string.Equals((s ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int SafeInt(string s)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static int? TryParseNullableInt(string s)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? (int?)v : null;
        }
    }

    public class ExportScheduleRoundtripExcelCommand : IRevitCommandHandler
    {
        public string CommandName => "export_schedule_roundtrip_excel";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = DocumentResolver.ResolveDocument(uiapp, cmd);
            if (doc == null) return new { ok = false, msg = "No target document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var schedule = ScheduleRoundtripExcelUtil.ResolveSchedule(uiapp, doc, p);
            if (schedule == null)
                return new { ok = false, msg = "ViewSchedule not found. Use active schedule or provide viewId/viewName." };

            string filePath = p.Value<string>("filePath")
                              ?? p.Value<string>("outputPath")
                              ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
                filePath = ScheduleRoundtripExcelUtil.GetDefaultExportPath(schedule);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Path.GetTempPath());

            bool autoFit = p.Value<bool?>("autoFit") ?? true;
            string requestedMode = (p.Value<string>("mode") ?? ScheduleRoundtripExcelUtil.ExportModeAuto).Trim().ToLowerInvariant();
            string exportMode = requestedMode;
            if (exportMode == ScheduleRoundtripExcelUtil.ExportModeAuto)
                exportMode = ScheduleRoundtripExcelUtil.DetermineEffectiveExportMode(schedule);
            else if (exportMode != ScheduleRoundtripExcelUtil.ExportModeDisplay)
                exportMode = ScheduleRoundtripExcelUtil.ExportModeRoundtrip;
            ElementId tempId = ElementId.InvalidElementId;

            try
            {
                var work = ScheduleRoundtripExcelUtil.PrepareTemporarySchedule(
                    doc,
                    schedule,
                    out tempId,
                    removeSortGroupFields: exportMode != ScheduleRoundtripExcelUtil.ExportModeDisplay);
                var rowElementIds = ScheduleRoundtripExcelUtil.ReadRowElementIds(doc, work);
                bool collectorFallbackUsed = false;
                if (rowElementIds.Count == 0)
                {
                    rowElementIds = ScheduleRoundtripExcelUtil.GetElementsInScheduleView(doc, work);
                    collectorFallbackUsed = rowElementIds.Count > 0;
                }
                var columnSourceSchedule = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay ? schedule : work;
                var cols = ScheduleRoundtripExcelUtil.BuildColumns(doc, columnSourceSchedule, rowElementIds);
                var displaySchedule = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay ? schedule : work;
                var table = displaySchedule.GetTableData();
                var body = table.GetSectionData(SectionType.Body);
                if (body == null)
                    return new { ok = false, msg = "Schedule body section not found." };
                int elementIdCol = ScheduleRoundtripExcelUtil.FindElementIdColumnIndex(work);
                var displayRowElementMap = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildDisplayRowElementMap(doc, schedule, work, cols)
                    : new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                var exactDisplayRowElementMap = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildExactDisplayRowElementMap(doc, work, cols)
                    : new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                var identityValueElementMap = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildIdentityValueElementMap(doc, rowElementIds, cols)
                    : new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                var itemizedDisplayRows = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildItemizedDisplayRows(doc, work, cols)
                    : new List<(int ElementId, IList<string> Values)>();

                using (var wb = new XLWorkbook(XLEventTracking.Disabled))
                {
                    var ws = wb.AddWorksheet(ScheduleRoundtripExcelUtil.DataSheetName);
                    var readme = wb.AddWorksheet(ScheduleRoundtripExcelUtil.ReadmeSheetName);
                    var meta = wb.AddWorksheet(ScheduleRoundtripExcelUtil.MetaSheetName);
                    var rowMapSheet = wb.AddWorksheet(ScheduleRoundtripExcelUtil.RowMapSheetName);
                    var rowMap = new Dictionary<int, List<int>>();

                    ScheduleRoundtripExcelUtil.WriteReadmeSheet(readme);
                    ScheduleRoundtripExcelUtil.WriteMetaSheet(meta, doc, schedule, cols);
                    meta.Cell(4, 2).Value = exportMode;

                    ws.Cell(1, 1).Value = ScheduleRoundtripExcelUtil.HiddenIdHeader;
                    ws.Column(1).Hide();

                    foreach (var col in cols)
                        ws.Cell(1, col.OutputColumnNumber).Value = col.Header;

                    int outRow = 2;
                    if (exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay)
                    {
                        var keyIndexes = ScheduleRoundtripExcelUtil.GetDisplayKeyColumnIndexes(schedule, cols);
                        for (int r = 0; r < body.NumberOfRows; r++)
                        {
                            var rowValues = ScheduleRoundtripExcelUtil.ReadDisplayRowValues(schedule, cols, r);
                            if (rowValues.Count == 0)
                                continue;

                            var exactKey = ScheduleRoundtripExcelUtil.BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                            exactDisplayRowElementMap.TryGetValue(exactKey, out var mappedIds);

                            if (mappedIds == null || mappedIds.Count == 0)
                            {
                                var rowKey = ScheduleRoundtripExcelUtil.BuildRowKeyFromValues(rowValues, keyIndexes);
                                if (!string.IsNullOrWhiteSpace(ScheduleRoundtripExcelUtil.NormalizeKeyText(rowKey)))
                                    displayRowElementMap.TryGetValue(rowKey, out mappedIds);
                            }

                            if (mappedIds == null || mappedIds.Count == 0)
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .MatchDisplayRowToElementIdsByIdentityValueMap(rowValues, cols, identityValueElementMap)
                                    .ToList();
                            }

                            if (mappedIds == null || mappedIds.Count == 0)
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .SelectiveMatchDisplayRowToElementIds(rowValues, cols, itemizedDisplayRows)
                                    .ToList();
                            }

                            ws.Cell(outRow, 1).Value = string.Join(";", mappedIds ?? new List<int>());
                            rowMap[outRow] = (mappedIds ?? new List<int>()).Distinct().ToList();

                            Element? representativeElement = null;
                            if (mappedIds != null)
                            {
                                foreach (var mappedId in mappedIds)
                                {
                                    representativeElement = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(mappedId));
                                    if (representativeElement != null)
                                        break;
                                }
                            }

                            for (int colIndex = 0; colIndex < cols.Count && colIndex < rowValues.Count; colIndex++)
                                ws.Cell(outRow, cols[colIndex].OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(
                                        representativeElement!,
                                        cols[colIndex],
                                        rowValues[colIndex]);

                            outRow++;
                        }
                    }
                    else if (collectorFallbackUsed)
                    {
                        foreach (var eid in rowElementIds)
                        {
                            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                            if (element == null) continue;

                            ws.Cell(outRow, 1).Value = eid;
                            rowMap[outRow] = new List<int> { eid };
                            foreach (var col in cols)
                            {
                                ws.Cell(outRow, col.OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(element, col, string.Empty);
                            }
                            outRow++;
                        }
                    }
                    else
                    {
                        for (int r = 0; r < body.NumberOfRows; r++)
                        {
                            var idText = ScheduleRoundtripExcelUtil.GetCellText(work, body, SectionType.Body, r, elementIdCol);
                            if (!int.TryParse((idText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                                continue;

                            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                            if (element == null) continue;

                            ws.Cell(outRow, 1).Value = eid;
                            rowMap[outRow] = new List<int> { eid };
                            foreach (var col in cols)
                            {
                                var displayText = ScheduleRoundtripExcelUtil.GetCellText(work, body, SectionType.Body, r, col.ScheduleColumnIndex);
                                ws.Cell(outRow, col.OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(element, col, displayText);
                            }
                            outRow++;
                        }
                    }

                    int lastRow = Math.Max(2, outRow - 1);
                    int lastCol = Math.Max(1, cols.Count + 1);

                    var headerRange = ws.Range(1, 1, 1, lastCol);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    var allRange = ws.Range(1, 1, lastRow, lastCol);
                    allRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    allRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    ScheduleRoundtripExcelUtil.ApplyColumnStyles(ws, cols, lastRow);
                    ws.SheetView.FreezeRows(1);
                    try { ws.Range(1, 1, lastRow, lastCol).SetAutoFilter(); } catch { }
                    ScheduleRoundtripExcelUtil.ApplyDataColumnWidths(ws, cols, lastRow, autoFit);
                    ScheduleRoundtripExcelUtil.WriteRowMapSheet(rowMapSheet, rowMap, exportMode);

                    wb.Worksheet(ScheduleRoundtripExcelUtil.DataSheetName).SetTabActive();
                    wb.SaveAs(filePath);
                }

                return new
                {
                    ok = true,
                    path = filePath,
                    requestedMode,
                    mode = exportMode,
                    scheduleViewId = schedule.Id.IntValue(),
                    scheduleName = schedule.Name,
                    itemizedRowCount = rowElementIds.Count,
                    exportedRowCount = Math.Max(0, (body?.NumberOfRows ?? 0)),
                    editableColumnCount = cols.Count(c => c.Editable),
                    booleanColumnCount = cols.Count(c => c.IsBoolean),
                    collectorFallbackUsed
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Roundtrip Excel export failed.", detail = ex.Message };
            }
            finally
            {
                ScheduleRoundtripExcelUtil.CleanupTempSchedule(doc, tempId);
            }
        }
    }

    public class ImportScheduleRoundtripExcelCommand : IRevitCommandHandler
    {
        public string CommandName => "import_schedule_roundtrip_excel";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = DocumentResolver.ResolveDocument(uiapp, cmd);
            if (doc == null) return new { ok = false, msg = "No target document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var filePath = p.Value<string>("filePath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
                return new { ok = false, msg = "filePath is required." };
            if (!File.Exists(filePath))
                return new { ok = false, msg = $"File not found: {filePath}" };

            var reportPath = p.Value<string>("reportPath") ?? ScheduleRoundtripExcelUtil.GetDefaultImportReportPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? Path.GetTempPath());

            try
            {
                using (var wb = new XLWorkbook(filePath, XLEventTracking.Disabled))
                using (var fhScope = new FailureHandlingScope(uiapp, FailureHandlingMode.Off))
                {
                    var dataSheetName = p.Value<string>("sheetName") ?? ScheduleRoundtripExcelUtil.DataSheetName;
                    var ws = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, dataSheetName, StringComparison.OrdinalIgnoreCase))
                             ?? wb.Worksheets.FirstOrDefault(x => !string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.ReadmeSheetName, StringComparison.OrdinalIgnoreCase));
                    if (ws == null)
                        return new { ok = false, msg = "Data sheet not found in workbook." };

                    var meta = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase));
                    if (meta == null)
                        return new { ok = false, msg = "Metadata sheet not found. Use export_schedule_roundtrip_excel output." };
                    var rowMapSheet = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.RowMapSheetName, StringComparison.OrdinalIgnoreCase));
                    var rowMap = ScheduleRoundtripExcelUtil.ReadRowMapSheet(rowMapSheet);

                    var cols = ScheduleRoundtripExcelUtil.ReadMetaSheet(meta).Where(c => c.Editable).ToList();
                    if (cols.Count == 0)
                        return new { ok = false, msg = "No editable columns found in metadata." };

                    int updatedCount = 0;
                    int skippedCount = 0;
                    int failedCount = 0;
                    var reportRows = new List<string> { "row,elementId,updated,skipped,failed,message" };
                    var exportMode = meta.Cell(4, 2).GetString();
                    int missingElementIdRows = 0;
                    ViewSchedule? displaySchedule = null;
                    ElementId tempId = ElementId.InvalidElementId;
                    IList<ScheduleRoundtripColumn> displayCols = Array.Empty<ScheduleRoundtripColumn>();
                    IList<(int ElementId, IList<string> Values)> itemizedDisplayRows = Array.Empty<(int ElementId, IList<string> Values)>();
                    IDictionary<string, int> worksheetHeaderColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    IDictionary<string, List<int>> identityValueElementMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        if (string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase))
                        {
                            var scheduleViewIdText = meta.Cell(2, 2).GetString();
                            if (int.TryParse(scheduleViewIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduleViewId))
                                displaySchedule = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(scheduleViewId)) as ViewSchedule;

                            if (displaySchedule == null)
                            {
                                var scheduleName = meta.Cell(3, 2).GetString();
                                if (!string.IsNullOrWhiteSpace(scheduleName))
                                {
                                    displaySchedule = new FilteredElementCollector(doc)
                                        .OfClass(typeof(ViewSchedule))
                                        .Cast<ViewSchedule>()
                                        .FirstOrDefault(v => !v.IsTemplate &&
                                                             string.Equals(v.Name, scheduleName, StringComparison.OrdinalIgnoreCase));
                                }
                            }

                            if (displaySchedule == null)
                                displaySchedule = uiapp.ActiveUIDocument?.ActiveView as ViewSchedule;

                            if (displaySchedule != null)
                            {
                                var mappingWork = ScheduleRoundtripExcelUtil.PrepareTemporarySchedule(
                                    doc,
                                    displaySchedule,
                                    out tempId,
                                    removeSortGroupFields: false);
                                var mappingRowElementIds = ScheduleRoundtripExcelUtil.ReadRowElementIds(doc, mappingWork);
                                if (mappingRowElementIds.Count == 0)
                                    mappingRowElementIds = ScheduleRoundtripExcelUtil.GetElementsInScheduleView(doc, mappingWork);
                                displayCols = ScheduleRoundtripExcelUtil.BuildColumns(doc, displaySchedule, mappingRowElementIds);
                                identityValueElementMap = ScheduleRoundtripExcelUtil.BuildIdentityValueElementMap(doc, mappingRowElementIds, displayCols);
                                itemizedDisplayRows = ScheduleRoundtripExcelUtil.BuildItemizedDisplayRows(doc, mappingWork, displayCols);
                                worksheetHeaderColumnMap = ScheduleRoundtripExcelUtil.BuildWorksheetHeaderColumnMap(ws);
                            }
                        }
                    }
                    catch { }

                    using (var tx = new Transaction(doc, "Import Schedule Roundtrip Excel"))
                    {
                        tx.Start();
                        TxnUtil.ConfigureProceedWithWarnings(tx);

                        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                        for (int row = 2; row <= lastRow; row++)
                        {
                            var idText = ws.Cell(row, 1).GetString();
                            var rowElementIds = ScheduleRoundtripExcelUtil.ParseElementIds(idText);
                            if (rowElementIds.Count == 0 && rowMap.TryGetValue(row, out var mappedRowIds))
                                rowElementIds = mappedRowIds;
                            if (rowElementIds.Count == 0
                                && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                && displayCols.Count > 0
                                && itemizedDisplayRows.Count > 0)
                            {
                                var displayRowValues = ScheduleRoundtripExcelUtil.ReadWorksheetDisplayRowValues(
                                    ws,
                                    row,
                                    displayCols,
                                    worksheetHeaderColumnMap);
                                rowElementIds = ScheduleRoundtripExcelUtil
                                    .MatchDisplayRowToElementIdsByIdentityValueMap(displayRowValues, displayCols, identityValueElementMap)
                                    .ToList();
                                if (rowElementIds.Count == 0)
                                {
                                    rowElementIds = ScheduleRoundtripExcelUtil
                                        .SelectiveMatchDisplayRowToElementIds(displayRowValues, displayCols, itemizedDisplayRows)
                                        .ToList();
                                }
                            }
                            if (rowElementIds.Count == 0)
                            {
                                skippedCount++;
                                missingElementIdRows++;
                                reportRows.Add($"{row},,0,1,0,missing-element-id");
                                continue;
                            }

                            int rowUpdated = 0;
                            int rowSkipped = 0;
                            int rowFailed = 0;
                            var rowMessages = new List<string>();

                            foreach (var col in cols)
                            {
                                var cellText = ws.Cell(row, col.OutputColumnNumber).GetString();
                                if (string.IsNullOrWhiteSpace(cellText))
                                {
                                    rowSkipped++;
                                    continue;
                                }

                                foreach (var eid in rowElementIds)
                                {
                                    var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                                    if (element == null)
                                    {
                                        rowFailed++;
                                        rowMessages.Add($"{col.Header}:element-not-found({eid})");
                                        continue;
                                    }

                                    var prm = ScheduleRoundtripExcelUtil.ResolveParameterOnElementOrType(element, col, out _, out _);
                                    if (prm == null || prm.IsReadOnly)
                                    {
                                        rowSkipped++;
                                        continue;
                                    }

                                    if (prm.StorageType == StorageType.ElementId
                                        && !int.TryParse((cellText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                    {
                                        rowSkipped++;
                                        continue;
                                    }

                                    if (ScheduleRoundtripExcelUtil.TryApplyImportedValue(prm, col, cellText, out var msg))
                                    {
                                        rowUpdated++;
                                    }
                                    else if (string.Equals(msg, "blank-skip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        rowSkipped++;
                                    }
                                    else
                                    {
                                        rowFailed++;
                                        rowMessages.Add($"{col.Header}[{eid}]:{msg}");
                                    }
                                }
                            }

                            updatedCount += rowUpdated;
                            skippedCount += rowSkipped;
                            failedCount += rowFailed;
                            reportRows.Add($"{row},\"{string.Join(";", rowElementIds)}\",{rowUpdated},{rowSkipped},{rowFailed},\"{string.Join(" | ", rowMessages).Replace("\"", "\"\"")}\"");
                        }

                        tx.Commit();
                    }

                    ScheduleRoundtripExcelUtil.CleanupTempSchedule(doc, tempId);

                    if (string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                        && updatedCount == 0
                        && missingElementIdRows > 0)
                    {
                        File.WriteAllLines(reportPath, reportRows);
                        return new
                        {
                            ok = false,
                            msg = "Display mode import mapping failed.",
                            detail = "display export rows could not be mapped back to element ids. Re-export with the latest addin.",
                            path = filePath,
                            reportPath,
                            mode = exportMode,
                            updatedCount,
                            skippedCount,
                            failedCount,
                            editableColumnCount = cols.Count
                        };
                    }

                    File.WriteAllLines(reportPath, reportRows);

                    return new
                    {
                        ok = true,
                        path = filePath,
                        reportPath,
                        mode = exportMode,
                        updatedCount,
                        skippedCount,
                        failedCount,
                        editableColumnCount = cols.Count,
                        failureHandling = new
                        {
                            enabled = false,
                            mode = "off",
                            issues = fhScope.Issues
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Roundtrip Excel import failed.", detail = ex.Message };
            }
        }
    }
}
