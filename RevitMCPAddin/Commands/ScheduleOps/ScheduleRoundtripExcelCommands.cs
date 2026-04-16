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
using Newtonsoft.Json;
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
        public string ResolvedScope { get; set; } = string.Empty; // instance | type | mixed | ""
    }

    internal sealed class ScheduleImportAuditElementResult
    {
        public int ElementId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Before { get; set; } = string.Empty;
        public string Imported { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    internal sealed class ScheduleImportAuditCellResult
    {
        public int OutputColumnNumber { get; set; }
        public string Header { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string ImportedValue { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<ScheduleImportAuditElementResult> Elements { get; set; } = new List<ScheduleImportAuditElementResult>();
    }

    internal sealed class ScheduleImportAuditRowResult
    {
        public int Row { get; set; }
        public string MappingSource { get; set; } = string.Empty;
        public List<int> ElementIds { get; set; } = new List<int>();
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<ScheduleImportAuditCellResult> Cells { get; set; } = new List<ScheduleImportAuditCellResult>();
    }

    internal sealed class ScheduleImportExpectedValue
    {
        public int Row { get; set; }
        public int OutputColumnNumber { get; set; }
        public int ElementId { get; set; }
        public string Header { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string ExpectedComparable { get; set; } = string.Empty;
        public string ExpectedDisplay { get; set; } = string.Empty;
        public string ImportedComparable { get; set; } = string.Empty;
        public bool CanApply { get; set; } = true;
    }

    internal sealed class ScheduleTargetRequest
    {
        public int Row { get; set; }
        public int OutputColumnNumber { get; set; }
        public int TargetElementId { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string ImportedComparable { get; set; } = string.Empty;
        public string ImportedDisplay { get; set; } = string.Empty;
    }

    internal sealed class ScheduleBaselineRow
    {
        public int RowNumber { get; set; }
        public string RowToken { get; set; } = string.Empty;
        public string RowIdentity { get; set; } = string.Empty;
        public string HiddenIdKey { get; set; } = string.Empty;
        public IList<string> Values { get; set; } = new List<string>();
    }

    internal sealed class ScheduleRowMapEntry
    {
        public int WorksheetRowNumber { get; set; }
        public string RowToken { get; set; } = string.Empty;
        public string RowIdentity { get; set; } = string.Empty;
        public List<int> ElementIds { get; set; } = new List<int>();
        public List<int> TypeIds { get; set; } = new List<int>();
        public string Scope { get; set; } = string.Empty;
        public int GroupCount { get; set; }
        public bool MeaningfulRow { get; set; }
        public string VisibleRowFingerprint { get; set; } = string.Empty;
    }

    internal sealed class ScheduleBaselineRowResolution
    {
        public int RowNumber { get; set; }
        public string Source { get; set; } = "row-number";
    }

    internal sealed class ScheduleRoundtripSupportAnalysis
    {
        public bool Supported { get; set; }
        public string StatusCode { get; set; } = "supported";
        public string ReasonCode { get; set; } = "DIRECT_ROUNDTRIP";
        public string Reason { get; set; } = string.Empty;
        public string SuggestedMode { get; set; } = ScheduleRoundtripExcelUtil.ExportModeAuto;
        public string CategoryName { get; set; } = string.Empty;
        public int VisibleColumnCount { get; set; }
    }

    internal static class ScheduleRoundtripExcelUtil
    {
        public const string ExportModeRoundtrip = "roundtrip";
        public const string ExportModeDisplay = "display";
        public const string ExportModeAuto = "auto";
        public const string HiddenIdHeader = "__ElementId";
        public const string HiddenRowTokenHeader = "__RowToken";
        public const string HiddenRowIdentityHeader = "__RowIdentity";
        public const string MetaSheetName = "__revit_roundtrip_meta";
        public const string RowMapSheetName = "__revit_roundtrip_rows";
        public const string BaselineSheetName = "__revit_roundtrip_snapshot";
        public const string DataSheetName = "Schedule";
        public const string ReadmeSheetName = "README";
        public const string SchemaVersionV2 = "schedule-roundtrip.v2";
        public const string RowResolutionModeAuthoritative = "rowtoken-authoritative";
        public const double MaxDataColumnWidth = 20d;
        public const double BooleanColumnWidth = 4.2d;
        private static readonly string[] ElementIdLikeNames =
        {
            "ID",
            "Element ID",
            "要素 ID",
            "要素ID"
        };

        private static readonly string[] ExchangeIdLikeNames =
        {
            "ID を交換",
            "Exchange ID"
        };

        private static readonly int[] ElementIdLikeParameterIds =
        {
            -1002100
        };

        private static readonly int[] ExchangeIdLikeParameterIds =
        {
            -1155400,
            -1155401
        };

        private static readonly string[] IdentityLikeHeaderKeywords =
        {
            "type",
            "type name",
            "family",
            "family and type",
            "name",
            "level",
            "mark",
            "\u30bf\u30a4\u30d7",
            "\u540d\u524d",
            "\u30ec\u30d9\u30eb",
            "\u968e",
            "\u7b26\u53f7",
            "\u30d5\u30a1\u30df\u30ea",
            "\u30d5\u30a1\u30df\u30ea\u3068\u30bf\u30a4\u30d7",
            "\u90e8\u5c4b\u756a\u53f7",
            "\u5ba4\u756a\u53f7",
            "\u90e8\u5c4b\u540d",
            "\u5ba4\u540d"
        };

        private static readonly string[] QuantityLikeHeaderKeywords =
        {
            "count",
            "qty",
            "quantity",
            "\u500b\u6570",
            "\u6570\u91cf"
        };

        private static readonly string[] LegendLikeKeywords =
        {
            "legend",
            "\u51e1\u4f8b",
            "keynote",
            "\u30ad\u30fc\u30ce\u30fc\u30c8",
            "schedule key",
            "\u7b26\u53f7\u9806\u5e8f\u8a2d\u5b9a"
        };

        private static readonly string[] KeyLikeKeywords =
        {
            "\u30ad\u30fc",
            "key schedule"
        };

        private static readonly string[] MaterialLikeKeywords =
        {
            "material",
            "\u30de\u30c6\u30ea\u30a2\u30eb",
            "\u6750\u6599",
            "\u90e8\u4f4d",
            "\u69cb\u6210",
            "\u30d1\u30fc\u30c4",
            "part"
        };

        private static readonly string[] NonElementSystemCategoryKeywords =
        {
            "\u30b7\u30fc\u30c8",
            "\u30d3\u30e5\u30fc",
            "\u30ec\u30d9\u30eb",
            "\u30ec\u30d9\u30eb\u7dda",
            "sheet",
            "sheets",
            "view",
            "views",
            "level",
            "levels"
        };

        private static readonly string[] NonElementSystemTitlePrefixes =
        {
            "* \u96c6\u8a08\u8868 \u30b7\u30fc\u30c8",
            "* \u96c6\u8a08\u8868 \u30d3\u30e5\u30fc",
            "* \u96c6\u8a08\u8868 \u30ec\u30d9\u30eb"
        };

        public static ViewSchedule? ResolveSchedule(UIApplication uiapp, Document doc, JObject p)
        {
            var viewId = p.Value<int?>("scheduleViewId")
                         ?? p.Value<int?>("scheduleId")
                         ?? p.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as ViewSchedule;

            var viewName = p.Value<string>("scheduleName")
                           ?? p.Value<string>("viewName");
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

        public static string GetScheduleCategoryName(Document doc, ViewSchedule schedule)
        {
            try
            {
                var catId = schedule?.Definition?.CategoryId;
                if (catId == null || catId == ElementId.InvalidElementId)
                    return string.Empty;

                return doc.Settings.Categories
                    .Cast<Category>()
                    .FirstOrDefault(c => c.Id == catId)
                    ?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static ScheduleRoundtripSupportAnalysis AnalyzeScheduleRoundtripSupport(Document doc, ViewSchedule schedule)
        {
            var categoryName = GetScheduleCategoryName(doc, schedule);
            var visibleHeaders = GetVisibleScheduleHeaders(schedule);
            int visibleColumnCount = visibleHeaders.Count(x =>
                !string.IsNullOrWhiteSpace(x)
                && !string.Equals(x, HiddenIdHeader, StringComparison.OrdinalIgnoreCase));
            var suggestedMode = DetermineEffectiveExportMode(schedule);
            bool hasIdentityLikeHeader = visibleHeaders.Any(IsIdentityLikeHeader);
            bool looksLikeLegendOrKey = LooksLikeLegendOrKeySchedule(schedule, categoryName, visibleHeaders);
            bool looksLikeMaterialOrPart = LooksLikeMaterialOrPartSchedule(schedule, categoryName, visibleHeaders);
            bool looksLikeNonElementSystemSchedule = LooksLikeNonElementSystemSchedule(schedule, categoryName, visibleHeaders);

            if (visibleColumnCount <= 0)
            {
                return new ScheduleRoundtripSupportAnalysis
                {
                    Supported = false,
                    StatusCode = "unsupported",
                    ReasonCode = "NO_VISIBLE_COLUMNS",
                    Reason = "対応不可: 可視列がありません。",
                    SuggestedMode = suggestedMode,
                    CategoryName = categoryName,
                    VisibleColumnCount = visibleColumnCount
                };
            }

            if (visibleColumnCount <= 1)
            {
                return new ScheduleRoundtripSupportAnalysis
                {
                    Supported = false,
                    StatusCode = "unsupported",
                    ReasonCode = "DISPLAY_SINGLE_COLUMN",
                    Reason = "対応不可: 表示モードの 1 列集計表は要素 ID に安全に対応付けできません。",
                    SuggestedMode = suggestedMode,
                    CategoryName = categoryName,
                    VisibleColumnCount = visibleColumnCount
                };
            }

            if (looksLikeNonElementSystemSchedule)
            {
                return new ScheduleRoundtripSupportAnalysis
                {
                    Supported = false,
                    StatusCode = "unsupported",
                    ReasonCode = "LIKELY_DOCUMENT_OR_VIEW_SCHEDULE",
                    Reason = "対応不可: シート・ビュー・レベル系の表と判定しました。",
                    SuggestedMode = suggestedMode,
                    CategoryName = categoryName,
                    VisibleColumnCount = visibleColumnCount
                };
            }

            if ((looksLikeLegendOrKey || looksLikeMaterialOrPart) && !hasIdentityLikeHeader)
            {
                return new ScheduleRoundtripSupportAnalysis
                {
                    Supported = false,
                    StatusCode = "unsupported",
                    ReasonCode = looksLikeLegendOrKey ? "LIKELY_LEGEND_OR_KEY_SCHEDULE" : "LIKELY_MATERIAL_OR_PART_SCHEDULE",
                    Reason = looksLikeLegendOrKey
                        ? "対応不可: 凡例・キー・注記系の表と判定しました。"
                        : "対応不可: 材料・部位構成系の表と判定しました。",
                    SuggestedMode = suggestedMode,
                    CategoryName = categoryName,
                    VisibleColumnCount = visibleColumnCount
                };
            }

            if (string.Equals(suggestedMode, ExportModeRoundtrip, StringComparison.OrdinalIgnoreCase))
            {
                return new ScheduleRoundtripSupportAnalysis
                {
                    Supported = true,
                    StatusCode = "supported",
                    ReasonCode = "DIRECT_ROUNDTRIP",
                    Reason = "要素ベースの往復編集に対応します。",
                    SuggestedMode = suggestedMode,
                    CategoryName = categoryName,
                    VisibleColumnCount = visibleColumnCount
                };
            }

            return new ScheduleRoundtripSupportAnalysis
            {
                Supported = true,
                StatusCode = "supported",
                ReasonCode = "DISPLAY_RUNTIME_CHECK",
                Reason = "表示順保持の display export 対象です。実行時に行マッピングを検証します。",
                SuggestedMode = suggestedMode,
                CategoryName = categoryName,
                VisibleColumnCount = visibleColumnCount
            };
        }

        private static IList<string> GetVisibleScheduleHeaders(ViewSchedule schedule)
        {
            try
            {
                return schedule.Definition
                    .GetFieldOrder()
                    .Select(fid => schedule.Definition.GetField(fid))
                    .Where(f => f != null && !f.IsHidden)
                    .Select(GetFieldHeader)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool LooksLikeLegendOrKeySchedule(ViewSchedule schedule, string categoryName, IList<string> visibleHeaders)
        {
            var corpus = string.Join(" | ",
                new[] { schedule?.Name ?? string.Empty, categoryName ?? string.Empty }
                    .Concat(visibleHeaders ?? Array.Empty<string>()));
            return ContainsAnyKeyword(corpus, LegendLikeKeywords) || ContainsAnyKeyword(corpus, KeyLikeKeywords);
        }

        private static bool LooksLikeMaterialOrPartSchedule(ViewSchedule schedule, string categoryName, IList<string> visibleHeaders)
        {
            var corpus = string.Join(" | ",
                new[] { schedule?.Name ?? string.Empty, categoryName ?? string.Empty }
                    .Concat(visibleHeaders ?? Array.Empty<string>()));
            return ContainsAnyKeyword(corpus, MaterialLikeKeywords);
        }

        private static bool LooksLikeNonElementSystemSchedule(ViewSchedule schedule, string categoryName, IList<string> visibleHeaders)
        {
            var normalizedCategory = NormalizeKeyText(categoryName ?? string.Empty);
            if (TextEqualsAny(normalizedCategory, NonElementSystemCategoryKeywords))
                return true;

            var normalizedTitle = NormalizeKeyText(schedule?.Name ?? string.Empty);
            if (StartsWithAny(normalizedTitle, NonElementSystemTitlePrefixes))
                return true;

            return false;
        }

        private static bool ContainsAnyKeyword(string text, IEnumerable<string> keywords)
        {
            var normalized = NormalizeKeyText(text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            foreach (var keyword in keywords ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;
                if (normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool TextEqualsAny(string text, IEnumerable<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var candidate in candidates ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (string.Equals(text, NormalizeKeyText(candidate), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool StartsWithAny(string text, IEnumerable<string> prefixes)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var prefix in prefixes ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;
                if (text.StartsWith(NormalizeKeyText(prefix), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

                // Auto mode should preserve the visible schedule whenever sort/group or totals affect row order/visibility.
                if (!isItemized) return ExportModeDisplay;
                if (sortGroupCount > 0) return ExportModeDisplay;
                if (showGrandTotals) return ExportModeDisplay;
            }
            catch { }

            return ExportModeRoundtrip;
        }

        public static bool IsScheduleItemized(ViewSchedule schedule)
        {
            if (schedule == null) return true;

            try
            {
                var def = schedule.Definition;
                var prop = def.GetType().GetProperty("IsItemized", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    return Convert.ToBoolean(prop.GetValue(def, null), CultureInfo.InvariantCulture);
            }
            catch { }

            return true;
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

        public static IList<int> MergeDistinctElementIds(IList<int>? preferred, IList<int>? additional)
        {
            var merged = new List<int>();
            var seen = new HashSet<int>();

            if (preferred != null)
            {
                foreach (var id in preferred)
                {
                    if (id <= 0 || seen.Contains(id))
                        continue;
                    seen.Add(id);
                    merged.Add(id);
                }
            }

            if (additional != null)
            {
                foreach (var id in additional)
                {
                    if (id <= 0 || seen.Contains(id))
                        continue;
                    seen.Add(id);
                    merged.Add(id);
                }
            }

            return merged;
        }

        public static int FindElementIdColumnIndex(ViewSchedule vs)
        {
            var def = vs.Definition;
            var visibleFields = def.GetFieldOrder()
                .Select(fid => def.GetField(fid))
                .Where(f => f != null && !f.IsHidden)
                .ToList();

            int bestHiddenHeaderIndex = -1;
            int bestHiddenHeaderPriority = int.MaxValue;
            for (int i = 0; i < visibleFields.Count; i++)
            {
                var field = visibleFields[i];
                if (field == null)
                    continue;

                if (!string.Equals(GetFieldHeader(field), HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                    continue;

                int priority = int.MaxValue;
                try
                {
                    var pid = field.ParameterId?.IntValue();
                    if (pid.HasValue)
                    {
                        var idx = Array.IndexOf(ElementIdLikeParameterIds, pid.Value);
                        if (idx >= 0)
                            priority = idx;
                    }
                }
                catch { }

                if (bestHiddenHeaderIndex < 0 || priority < bestHiddenHeaderPriority)
                {
                    bestHiddenHeaderIndex = i;
                    bestHiddenHeaderPriority = priority;
                }
            }
            if (bestHiddenHeaderIndex >= 0 && bestHiddenHeaderPriority < int.MaxValue)
                return bestHiddenHeaderIndex;

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

            try
            {
                var pid = field.ParameterId?.IntValue();
                if (IsExchangeIdLikeParameterId(pid))
                    return false;
                if (IsElementIdLikeParameterId(pid))
                    return true;
            }
            catch { }

            var header = GetFieldHeader(field);
            if (string.Equals(header, HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsExchangeIdLikeName(header))
                return false;

            foreach (var candidate in ElementIdLikeNames)
            {
                if (string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            try
            {
                var sourceName = field.GetName() ?? string.Empty;
                if (IsExchangeIdLikeName(sourceName))
                    return false;
                foreach (var candidate in ElementIdLikeNames)
                {
                    if (string.Equals(sourceName, candidate, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            return false;
        }

        public static bool IsElementIdLikeSchedulableField(Document doc, SchedulableField sf)
        {
            if (sf == null) return false;

            try
            {
                var pid = sf.ParameterId?.IntValue();
                if (IsExchangeIdLikeParameterId(pid))
                    return false;
                if (IsElementIdLikeParameterId(pid))
                    return true;
            }
            catch { }

            try
            {
                var name = sf.GetName(doc) ?? string.Empty;
                if (IsExchangeIdLikeName(name))
                    return false;
                foreach (var candidate in ElementIdLikeNames)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsExchangeIdLikeName(string? name)
        {
            var normalized = NormalizeKeyText(name ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            foreach (var candidate in ExchangeIdLikeNames)
            {
                if (string.Equals(normalized, NormalizeKeyText(candidate), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsExchangeIdLikeParameterId(int? parameterId)
        {
            return parameterId.HasValue && Array.IndexOf(ExchangeIdLikeParameterIds, parameterId.Value) >= 0;
        }

        private static bool IsElementIdLikeParameterId(int? parameterId)
        {
            return parameterId.HasValue && Array.IndexOf(ElementIdLikeParameterIds, parameterId.Value) >= 0;
        }

        private static SchedulableField? FindPreferredElementIdSchedulableField(
            Document doc,
            IEnumerable<SchedulableField> available)
        {
            var fields = (available ?? Enumerable.Empty<SchedulableField>())
                .Where(sf => sf != null)
                .ToList();
            if (fields.Count == 0)
                return null;

            var byParameterId = fields.FirstOrDefault(sf =>
            {
                try
                {
                    var pid = sf.ParameterId?.IntValue();
                    if (IsExchangeIdLikeParameterId(pid))
                        return false;
                    return IsElementIdLikeParameterId(pid);
                }
                catch
                {
                    return false;
                }
            });
            if (byParameterId != null)
                return byParameterId;

            var byName = fields.FirstOrDefault(sf =>
            {
                try
                {
                    var pid = sf.ParameterId?.IntValue();
                    if (IsExchangeIdLikeParameterId(pid))
                        return false;
                    var name = sf.GetName(doc) ?? string.Empty;
                    if (IsExchangeIdLikeName(name))
                        return false;
                    return ElementIdLikeNames.Any(candidate =>
                        string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            });
            if (byName != null)
                return byName;

            return fields.FirstOrDefault(sf => IsElementIdLikeSchedulableField(doc, sf));
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

        public static string NormalizeResolvedScope(string scope)
        {
            var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(normalized, "instance", StringComparison.Ordinal))
                return "instance";
            if (string.Equals(normalized, "type", StringComparison.Ordinal))
                return "type";
            if (string.Equals(normalized, "mixed", StringComparison.Ordinal))
                return "mixed";
            return string.Empty;
        }

        public static bool IsTypeLikeScope(string scope)
        {
            var normalized = NormalizeResolvedScope(scope);
            return string.Equals(normalized, "type", StringComparison.Ordinal)
                || string.Equals(normalized, "mixed", StringComparison.Ordinal);
        }

        public static string BuildExpectedValueKey(int row, int outputColumnNumber, int elementId)
        {
            return string.Concat(
                row.ToString(CultureInfo.InvariantCulture), "|",
                outputColumnNumber.ToString(CultureInfo.InvariantCulture), "|",
                elementId.ToString(CultureInfo.InvariantCulture));
        }

        public static string BuildExpectedCellKey(int row, int outputColumnNumber)
        {
            return string.Concat(
                row.ToString(CultureInfo.InvariantCulture), "|",
                outputColumnNumber.ToString(CultureInfo.InvariantCulture));
        }

        public static string BuildTargetRequestKey(int outputColumnNumber, int targetElementId)
        {
            return string.Concat(
                outputColumnNumber.ToString(CultureInfo.InvariantCulture),
                "|",
                targetElementId.ToString(CultureInfo.InvariantCulture));
        }

        public static string BuildResolvedTargetKey(Element? owner, string scope, int fallbackElementId)
        {
            var targetId = owner?.Id != null && owner.Id != ElementId.InvalidElementId
                ? owner.Id.IntValue()
                : fallbackElementId;
            var normalizedScope = NormalizeResolvedScope(scope);
            if (string.IsNullOrWhiteSpace(normalizedScope))
                normalizedScope = "instance";
            return string.Concat(
                normalizedScope,
                "|",
                targetId.ToString(CultureInfo.InvariantCulture));
        }

        public static int GetResolvedTargetElementId(Element? owner, int fallbackElementId)
        {
            if (owner?.Id != null && owner.Id != ElementId.InvalidElementId)
                return owner.Id.IntValue();
            return fallbackElementId;
        }

        public static IDictionary<string, ScheduleImportExpectedValue> ReadExpectedValues(JToken? token)
        {
            var map = new Dictionary<string, ScheduleImportExpectedValue>(StringComparer.OrdinalIgnoreCase);
            var items = token as JArray;
            if (items == null)
                return map;

            foreach (var item in items.OfType<JObject>())
            {
                var row = item.Value<int?>("row") ?? 0;
                var outputColumnNumber = item.Value<int?>("outputColumnNumber") ?? 0;
                var elementId = item.Value<int?>("elementId") ?? 0;
                if (row <= 0 || outputColumnNumber <= 0 || elementId <= 0)
                    continue;

                var entry = new ScheduleImportExpectedValue
                {
                    Row = row,
                    OutputColumnNumber = outputColumnNumber,
                    ElementId = elementId,
                    Header = item.Value<string>("header") ?? string.Empty,
                    ParameterName = item.Value<string>("parameterName") ?? string.Empty,
                    ExpectedComparable = item.Value<string>("expectedComparable") ?? string.Empty,
                    ExpectedDisplay = item.Value<string>("expectedDisplay") ?? string.Empty,
                    ImportedComparable = item.Value<string>("importedComparable") ?? string.Empty,
                    CanApply = item.Value<bool?>("canApply") ?? true
                };
                map[BuildExpectedValueKey(row, outputColumnNumber, elementId)] = entry;
            }

            return map;
        }

        public static IDictionary<string, List<ScheduleImportExpectedValue>> GroupExpectedValuesByCell(
            IEnumerable<ScheduleImportExpectedValue>? expectedValues)
        {
            var map = new Dictionary<string, List<ScheduleImportExpectedValue>>(StringComparer.OrdinalIgnoreCase);
            if (expectedValues == null)
                return map;

            foreach (var item in expectedValues)
            {
                if (item == null || item.Row <= 0 || item.OutputColumnNumber <= 0 || item.ElementId <= 0)
                    continue;

                var key = BuildExpectedCellKey(item.Row, item.OutputColumnNumber);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<ScheduleImportExpectedValue>();
                    map[key] = list;
                }

                if (!list.Any(x => x.ElementId == item.ElementId))
                    list.Add(item);
            }

            return map;
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

        public static Parameter? ResolveParameterByFixedScope(Element? element, ScheduleRoundtripColumn col, out Element? owner, out string scope)
        {
            owner = element;
            scope = "none";
            if (element == null) return null;

            var desiredScope = NormalizeResolvedScope(col.ResolvedScope);
            if (string.Equals(desiredScope, "mixed", StringComparison.Ordinal))
            {
                owner = element;
                scope = "mixed";
                return null;
            }

            var payload = BuildResolverPayload(col);
            if (string.Equals(desiredScope, "instance", StringComparison.Ordinal))
            {
                var prm = ParamResolver.ResolveByPayload(element, payload, out _);
                if (prm != null)
                {
                    owner = element;
                    scope = "instance";
                    return prm;
                }

                owner = element;
                scope = "instance";
                return null;
            }

            if (string.Equals(desiredScope, "type", StringComparison.Ordinal))
            {
                try
                {
                    var typeId = element.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = element.Document.GetElement(typeId);
                        if (typeElem != null)
                        {
                            var prm = ParamResolver.ResolveByPayload(typeElem, payload, out _);
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
                scope = "type";
                return null;
            }

            return ResolveParameterOnElementOrType(element, col, out owner, out scope);
        }

        public static Parameter? ResolveParameterOnExplicitTarget(Element? element, ScheduleRoundtripColumn col, out Element? owner, out string scope)
        {
            owner = element;
            scope = "none";
            if (element == null) return null;

            var desiredScope = NormalizeResolvedScope(col.ResolvedScope);
            var payload = BuildResolverPayload(col);

            if (string.Equals(desiredScope, "type", StringComparison.Ordinal) && element is ElementType)
            {
                var prm = ParamResolver.ResolveByPayload(element, payload, out _);
                if (prm != null)
                {
                    owner = element;
                    scope = "type";
                    return prm;
                }
            }

            if (string.Equals(desiredScope, "instance", StringComparison.Ordinal) || element is ElementType)
            {
                var prm = ParamResolver.ResolveByPayload(element, payload, out _);
                if (prm != null)
                {
                    owner = element;
                    scope = string.Equals(desiredScope, "type", StringComparison.Ordinal) ? "type" : "instance";
                    return prm;
                }
            }

            return ResolveParameterByFixedScope(element, col, out owner, out scope);
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
                var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rowElement in rowElements)
                {
                    var candidate = ResolveParameterOnElementOrType(rowElement, probeCol, out _, out var candidateScope);
                    if (candidate == null)
                        continue;

                    prm ??= candidate;

                    var normalizedScope = NormalizeResolvedScope(candidateScope);
                    if (!string.IsNullOrWhiteSpace(normalizedScope))
                        scopes.Add(normalizedScope);

                    if (IsYesNoParameter(candidate))
                        isBoolean = true;

                    if (!candidate.IsReadOnly && candidate.StorageType != StorageType.ElementId)
                    {
                        editable = true;
                        if (prm == null || prm.IsReadOnly || prm.StorageType == StorageType.ElementId)
                            prm = candidate;
                    }
                }

                var resolvedScope = scopes.Count == 1
                    ? scopes.First()
                    : scopes.Count > 1
                        ? "mixed"
                        : string.Empty;

                if (string.Equals(resolvedScope, "mixed", StringComparison.OrdinalIgnoreCase))
                    editable = false;

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
                    SourceFieldName = sourceFieldName,
                    ResolvedScope = resolvedScope
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

        public static IList<string> ReadDisplayRowValuesAlignedWithoutElementId(ViewSchedule vs, IList<ScheduleRoundtripColumn> cols, int rowRel)
        {
            var values = new List<string>();
            var table = vs.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null) return values;

            var visibleFields = vs.Definition.GetFieldOrder()
                .Select(fid => vs.Definition.GetField(fid))
                .Where(f => f != null && !f.IsHidden)
                .ToList();

            var nonIdColumnIndexes = new List<int>();
            for (int i = 0; i < visibleFields.Count; i++)
            {
                if (!IsElementIdLikeField(visibleFields[i]))
                    nonIdColumnIndexes.Add(i);
            }

            for (int i = 0; i < cols.Count && i < nonIdColumnIndexes.Count; i++)
                values.Add(GetCellText(vs, body, SectionType.Body, rowRel, nonIdColumnIndexes[i]));

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

        private static IList<(int RowIndex, int ElementId)> EnumerateMappingRowsWithElementIds(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var pairs = new List<(int RowIndex, int ElementId)>();
            var workTable = mappingWork.GetTableData();
            var workBody = workTable.GetSectionData(SectionType.Body);
            if (workBody == null)
                return pairs;

            int elementIdCol = FindElementIdColumnIndex(mappingWork);
            if (elementIdCol >= 0)
            {
                for (int r = 0; r < workBody.NumberOfRows; r++)
                {
                    var idText = GetCellText(mappingWork, workBody, SectionType.Body, r, elementIdCol);
                    if (!int.TryParse((idText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                        continue;
                    if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) == null)
                        continue;
                    pairs.Add((r, eid));
                }
            }

            var pairMap = new Dictionary<int, int>();
            foreach (var pair in pairs)
            {
                if (!pairMap.ContainsKey(pair.RowIndex))
                    pairMap[pair.RowIndex] = pair.ElementId;
            }

            var fallbackPairs = EnumerateMappingRowsWithFallbackMatching(
                doc,
                mappingWork,
                cols,
                fallbackElementIds,
                pairMap);
            foreach (var pair in fallbackPairs)
            {
                if (!pairMap.ContainsKey(pair.RowIndex))
                    pairMap[pair.RowIndex] = pair.ElementId;
            }

            return pairMap
                .OrderBy(x => x.Key)
                .Select(x => (x.Key, x.Value))
                .ToList();
        }

        private static IList<(int RowIndex, int ElementId)> EnumerateMappingRowsWithFallbackMatching(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds,
            IDictionary<int, int>? existingPairs = null)
        {
            var pairs = new List<(int RowIndex, int ElementId)>();
            if (doc == null || mappingWork == null || cols == null || cols.Count == 0 || fallbackElementIds == null || fallbackElementIds.Count == 0)
                return pairs;

            var workTable = mappingWork.GetTableData();
            var workBody = workTable.GetSectionData(SectionType.Body);
            if (workBody == null || workBody.NumberOfRows <= 0)
                return pairs;

            var candidateIds = fallbackElementIds
                .Where(id => id > 0 && doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) != null)
                .Distinct()
                .ToList();
            if (candidateIds.Count == 0)
                return pairs;

            var liveRows = BuildLiveElementRows(doc, candidateIds, cols);
            if (liveRows.Count == 0)
                return pairs;

            var displayKeyIndexes = GetDisplayKeyColumnIndexes(mappingWork, cols);
            var consumedIds = new HashSet<int>((existingPairs ?? new Dictionary<int, int>()).Values.Where(x => x > 0));

            for (int rowIndex = 0; rowIndex < workBody.NumberOfRows; rowIndex++)
            {
                if (existingPairs != null && existingPairs.ContainsKey(rowIndex))
                    continue;

                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, rowIndex);
                if (rowValues.Count == 0)
                    continue;
                if (IsDisplayHeaderLikeRow(rowValues, cols))
                    continue;
                if (CountMeaningfulDisplayValues(rowValues, cols) <= 0)
                    continue;
                if (IsLikelyDisplayNonElementRow(rowValues, cols, displayKeyIndexes))
                    continue;

                var remainingRows = liveRows
                    .Where(x => x.ElementId > 0 && !consumedIds.Contains(x.ElementId))
                    .ToList();
                if (remainingRows.Count == 0)
                    break;

                var remainingIdentityValueElementMap = BuildIdentityValueElementMap(
                    doc,
                    remainingRows.Select(x => x.ElementId).ToList(),
                    cols);

                var matchedIds = MatchDisplayRowToLiveElementIds(
                        rowValues,
                        cols,
                        remainingIdentityValueElementMap,
                        remainingRows)
                    .Where(x => !consumedIds.Contains(x))
                    .Distinct()
                    .ToList();

                if (matchedIds.Count != 1)
                {
                    var exactMatches = SelectiveMatchDisplayRowToElementIds(rowValues, cols, remainingRows)
                        .Where(x => !consumedIds.Contains(x))
                        .Distinct()
                        .ToList();
                    if (exactMatches.Count == 1)
                    {
                        matchedIds = exactMatches;
                    }
                    else if (exactMatches.Count > 1)
                    {
                        matchedIds = exactMatches;
                    }
                    else
                    {
                        var ambiguousExactMatch = TryResolveAmbiguousLiveRowMatch(rowValues, remainingRows, exactMatches);
                        if (ambiguousExactMatch.HasValue)
                            matchedIds = new List<int> { ambiguousExactMatch.Value };
                    }
                }

                if (matchedIds.Count > 1)
                {
                    var runLength = CountConsecutiveEquivalentMappingRows(
                        mappingWork,
                        cols,
                        rowIndex,
                        existingPairs);
                    if (runLength > 0)
                    {
                        var runIds = TakeElementIdsInLiveRowOrder(remainingRows, matchedIds, runLength);
                        if (runIds.Count == runLength)
                        {
                            for (int offset = 0; offset < runIds.Count; offset++)
                            {
                                var resolvedId = runIds[offset];
                                consumedIds.Add(resolvedId);
                                pairs.Add((rowIndex + offset, resolvedId));
                            }

                            rowIndex += runIds.Count - 1;
                            continue;
                        }
                    }
                }

                if (matchedIds.Count != 1)
                    continue;

                consumedIds.Add(matchedIds[0]);
                pairs.Add((rowIndex, matchedIds[0]));
            }

            return pairs;
        }

        private static int CountConsecutiveEquivalentMappingRows(
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            int startRowIndex,
            IDictionary<int, int>? existingPairs = null)
        {
            if (mappingWork == null || cols == null || cols.Count == 0 || startRowIndex < 0)
                return 0;

            var table = mappingWork.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null || startRowIndex >= body.NumberOfRows)
                return 0;

            var firstValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, startRowIndex);
            if (firstValues.Count == 0)
                return 0;

            var firstKey = BuildVisibleRowKey(firstValues);
            if (string.IsNullOrWhiteSpace(firstKey))
                return 0;

            int count = 0;
            for (int rowIndex = startRowIndex; rowIndex < body.NumberOfRows; rowIndex++)
            {
                if (existingPairs != null && existingPairs.ContainsKey(rowIndex))
                    break;

                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, rowIndex);
                if (rowValues.Count == 0)
                    break;

                if (!string.Equals(BuildVisibleRowKey(rowValues), firstKey, StringComparison.OrdinalIgnoreCase))
                    break;

                count++;
            }

            return count;
        }

        private static int? TryResolveAmbiguousLiveRowMatch(
            IList<string> rowValues,
            IList<(int ElementId, IList<string> Values)> candidateRows,
            IList<int>? matchedIds)
        {
            if (rowValues == null || candidateRows == null || matchedIds == null || matchedIds.Count <= 1)
                return null;

            var targetKey = BuildVisibleRowKey(rowValues);
            if (string.IsNullOrWhiteSpace(targetKey))
                return null;

            var matchedRows = candidateRows
                .Where(x => matchedIds.Contains(x.ElementId))
                .ToList();
            if (matchedRows.Count != matchedIds.Count)
                return null;

            if (!matchedRows.All(x => string.Equals(BuildVisibleRowKey(x.Values), targetKey, StringComparison.OrdinalIgnoreCase)))
                return null;

            return matchedRows[0].ElementId;
        }

        public static IDictionary<string, List<int>> BuildDisplayRowElementMap(
            Document doc,
            ViewSchedule source,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var keyIndexes = GetDisplayKeyColumnIndexes(source, cols);
            foreach (var pair in EnumerateMappingRowsWithElementIds(doc, mappingWork, cols, fallbackElementIds))
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, pair.RowIndex);

                var key = BuildRowKeyFromValues(rowValues, keyIndexes);
                if (!map.TryGetValue(key, out var ids))
                {
                    ids = new List<int>();
                    map[key] = ids;
                }

                if (!ids.Contains(pair.ElementId))
                    ids.Add(pair.ElementId);
            }

            return map;
        }

        public static IDictionary<string, List<int>> BuildExactDisplayRowElementMap(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in EnumerateMappingRowsWithElementIds(doc, mappingWork, cols, fallbackElementIds))
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, pair.RowIndex);

                var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                if (!map.TryGetValue(key, out var ids))
                {
                    ids = new List<int>();
                    map[key] = ids;
                }

                if (!ids.Contains(pair.ElementId))
                    ids.Add(pair.ElementId);
            }

            return map;
        }

        public static IDictionary<string, Queue<List<int>>> BuildOrderedExactDisplayRowElementBuckets(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var map = new Dictionary<string, Queue<List<int>>>(StringComparer.OrdinalIgnoreCase);
            string? currentKey = null;
            List<int>? currentBucket = null;

            var pairs = EnumerateMappingRowsWithElementIds(doc, mappingWork, cols, fallbackElementIds)
                .ToList();
            foreach (var pair in pairs)
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, pair.RowIndex);
                var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                if (currentBucket == null
                    || !string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    currentKey = key;
                    currentBucket = new List<int>();
                    if (!map.TryGetValue(key, out var buckets))
                    {
                        buckets = new Queue<List<int>>();
                        map[key] = buckets;
                    }

                    buckets.Enqueue(currentBucket);
                }

                if (!currentBucket.Contains(pair.ElementId))
                    currentBucket.Add(pair.ElementId);
            }

            if (map.Count > 0)
                return map;

            foreach (var bucket in BuildDisplayBucketsByLiveMatching(doc, mappingWork, cols, fallbackElementIds))
            {
                var key = BuildRowKeyFromValues(bucket.Values, Enumerable.Range(0, bucket.Values.Count).ToList());
                if (!map.TryGetValue(key, out var buckets))
                {
                    buckets = new Queue<List<int>>();
                    map[key] = buckets;
                }

                buckets.Enqueue(bucket.ElementIds.Distinct().OrderBy(x => x).ToList());
            }

            return map;
        }

        private static IList<(IList<string> Values, List<int> ElementIds)> BuildDisplayBucketsByLiveMatching(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds)
        {
            var buckets = new List<(IList<string> Values, List<int> ElementIds)>();
            if (doc == null || mappingWork == null || cols == null || cols.Count == 0 || fallbackElementIds == null || fallbackElementIds.Count == 0)
                return buckets;

            var table = mappingWork.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows <= 0)
                return buckets;

            var remainingRows = BuildLiveElementRows(doc, fallbackElementIds.Distinct().ToList(), cols)
                .ToList();
            if (remainingRows.Count == 0)
                return buckets;

            var displayKeyIndexes = GetDisplayKeyColumnIndexes(mappingWork, cols);
            for (int rowIndex = 0; rowIndex < body.NumberOfRows; rowIndex++)
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, rowIndex);
                if (rowValues.Count == 0)
                    continue;
                if (IsDisplayHeaderLikeRow(rowValues, cols))
                    continue;
                if (CountMeaningfulDisplayValues(rowValues, cols) <= 0)
                    continue;
                if (IsLikelyDisplayNonElementRow(rowValues, cols, displayKeyIndexes))
                    continue;

                var expectedCount = TryGetExpectedElementCount(rowValues, cols, sourceIsItemized: false) ?? 1;
                if (expectedCount <= 0)
                    continue;

                var matchedIds = ResolveDisplayValueRunElementIds(doc, rowValues, expectedCount, cols, remainingRows);
                if (matchedIds.Count <= 0)
                    continue;

                buckets.Add((rowValues, matchedIds.Distinct().OrderBy(x => x).ToList()));
                var consumed = new HashSet<int>(matchedIds);
                remainingRows = remainingRows
                    .Where(x => !consumed.Contains(x.ElementId))
                    .ToList();
            }

            return buckets;
        }

        private static IList<(IList<string> Values, int Count)> EnumerateDisplayValueRuns(
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols)
        {
            var runs = new List<(IList<string> Values, int Count)>();
            if (mappingWork == null || cols == null || cols.Count == 0)
                return runs;

            var table = mappingWork.GetTableData();
            var body = table.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows <= 0)
                return runs;

            string? currentKey = null;
            IList<string>? currentValues = null;
            int currentCount = 0;
            for (int rowIndex = 0; rowIndex < body.NumberOfRows; rowIndex++)
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, rowIndex);
                if (rowValues.Count == 0)
                    continue;
                if (IsDisplayHeaderLikeRow(rowValues, cols))
                    continue;
                if (CountMeaningfulDisplayValues(rowValues, cols) <= 0)
                    continue;

                var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                if (currentValues == null
                    || !string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentValues != null && currentCount > 0)
                        runs.Add((currentValues, currentCount));

                    currentKey = key;
                    currentValues = rowValues;
                    currentCount = 1;
                }
                else
                {
                    currentCount++;
                }
            }

            if (currentValues != null && currentCount > 0)
                runs.Add((currentValues, currentCount));

            return runs;
        }

        private static IList<int> ResolveDisplayValueRunElementIds(
            Document doc,
            IList<string> rowValues,
            int expectedCount,
            IList<ScheduleRoundtripColumn> cols,
            IList<(int ElementId, IList<string> Values)> remainingRows)
        {
            if (doc == null || rowValues == null || cols == null || expectedCount <= 0 || remainingRows == null || remainingRows.Count == 0)
                return Array.Empty<int>();

            var strictIndexes = BuildDisplayMatchIndexes(rowValues, cols, null, includeAllMeaningful: true);
            var strictMatches = MatchDisplayRowToElementIdsByIndexes(rowValues, remainingRows, strictIndexes)
                .Distinct()
                .ToList();
            var ids = TakeElementIdsInLiveRowOrder(remainingRows, strictMatches, expectedCount);
            if (ids.Count == expectedCount)
                return ids;

            var primaryIndexes = BuildDisplayMatchIndexes(rowValues, cols, null, includeAllMeaningful: false);
            if (!strictIndexes.SequenceEqual(primaryIndexes))
            {
                var primaryMatches = MatchDisplayRowToElementIdsByIndexes(rowValues, remainingRows, primaryIndexes)
                    .Distinct()
                    .ToList();
                ids = TakeElementIdsInLiveRowOrder(remainingRows, primaryMatches, expectedCount);
                if (ids.Count == expectedCount)
                    return ids;
            }

            var remainingIds = remainingRows
                .Select(x => x.ElementId)
                .Distinct()
                .ToList();
            var identityValueElementMap = BuildIdentityValueElementMap(doc, remainingIds, cols);
            var fallbackMatches = MatchDisplayRowToLiveElementIds(rowValues, cols, identityValueElementMap, remainingRows)
                .Distinct()
                .ToList();
            ids = TakeElementIdsInLiveRowOrder(remainingRows, fallbackMatches, expectedCount);
            if (ids.Count == expectedCount)
                return ids;

            return Array.Empty<int>();
        }

        private static IList<int> TakeElementIdsInLiveRowOrder(
            IList<(int ElementId, IList<string> Values)> remainingRows,
            IEnumerable<int>? matchedIds,
            int expectedCount)
        {
            if (remainingRows == null || matchedIds == null || expectedCount <= 0)
                return Array.Empty<int>();

            var allowed = new HashSet<int>(matchedIds.Where(x => x > 0));
            if (allowed.Count < expectedCount)
                return Array.Empty<int>();

            var ordered = remainingRows
                .Where(x => allowed.Contains(x.ElementId))
                .Select(x => x.ElementId)
                .Distinct()
                .Take(expectedCount)
                .ToList();

            if (ordered.Count == expectedCount)
                return ordered;

            return Array.Empty<int>();
        }

        public static IDictionary<string, Queue<int>> BuildOrderedExactDisplayRowElementQueues(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var map = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in EnumerateMappingRowsWithElementIds(doc, mappingWork, cols, fallbackElementIds))
            {
                var rowValues = ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, pair.RowIndex);
                var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
                if (!map.TryGetValue(key, out var queue))
                {
                    queue = new Queue<int>();
                    map[key] = queue;
                }

                queue.Enqueue(pair.ElementId);
            }

            return map;
        }

        public static IList<int> ConsumeOrderedExactDisplayRowElementBucket(
            IDictionary<string, Queue<List<int>>> bucketMap,
            IList<string> rowValues)
        {
            if (bucketMap == null || rowValues == null)
                return Array.Empty<int>();

            var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
            if (!bucketMap.TryGetValue(key, out var buckets) || buckets == null || buckets.Count == 0)
                return Array.Empty<int>();

            return buckets.Dequeue().Distinct().OrderBy(x => x).ToList();
        }

        public static IList<int> ConsumeOrderedExactDisplayRowElementQueue(
            IDictionary<string, Queue<int>> queueMap,
            IList<string> rowValues)
        {
            if (queueMap == null || rowValues == null)
                return Array.Empty<int>();

            var key = BuildRowKeyFromValues(rowValues, Enumerable.Range(0, rowValues.Count).ToList());
            if (!queueMap.TryGetValue(key, out var queue) || queue == null || queue.Count == 0)
                return Array.Empty<int>();

            return new[] { queue.Dequeue() };
        }

        public static IList<(int ElementId, IList<string> Values)> BuildItemizedDisplayRows(
            Document doc,
            ViewSchedule mappingWork,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? fallbackElementIds = null)
        {
            var rows = new List<(int ElementId, IList<string> Values)>();
            foreach (var pair in EnumerateMappingRowsWithElementIds(doc, mappingWork, cols, fallbackElementIds))
            {
                rows.Add((pair.ElementId, ReadDisplayRowValuesAlignedWithoutElementId(mappingWork, cols, pair.RowIndex)));
            }

            return rows;
        }

        public static IList<(int ElementId, IList<string> Values)> BuildLiveElementRows(
            Document doc,
            IList<int> elementIds,
            IList<ScheduleRoundtripColumn> cols)
        {
            var rows = new List<(int ElementId, IList<string> Values)>();
            if (doc == null || elementIds == null || cols == null || cols.Count == 0)
                return rows;

            foreach (var eid in elementIds.Distinct())
            {
                var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                if (element == null)
                    continue;

                var values = new List<string>();
                foreach (var col in cols)
                    values.Add(GetExportValueForColumn(element, col, string.Empty));

                rows.Add((eid, values));
            }

            return rows;
        }

        private static IEnumerable<string> GetColumnIdentityNames(ScheduleRoundtripColumn? col)
        {
            if (col == null)
                yield break;

            if (!string.IsNullOrWhiteSpace(col.Header))
                yield return col.Header;
            if (!string.IsNullOrWhiteSpace(col.SourceFieldName))
                yield return col.SourceFieldName;
            if (!string.IsNullOrWhiteSpace(col.ParamName))
                yield return col.ParamName;
        }

        private static bool ColumnIdentityEquals(ScheduleRoundtripColumn? col, params string[] candidates)
        {
            if (col == null || candidates == null || candidates.Length == 0)
                return false;

            foreach (var name in GetColumnIdentityNames(col))
            {
                var normalizedName = NormalizeKeyText(name);
                foreach (var candidate in candidates)
                {
                    if (string.Equals(normalizedName, NormalizeKeyText(candidate), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetElementTypeInfo(Element? element, out ElementType? typeElem, out string familyName, out string typeName)
        {
            typeElem = null;
            familyName = string.Empty;
            typeName = string.Empty;
            if (element == null)
                return false;

            try
            {
                typeElem = element as ElementType;
                if (typeElem == null)
                {
                    var typeId = element.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                        typeElem = element.Document.GetElement(typeId) as ElementType;
                }
            }
            catch { }

            if (typeElem == null)
                return false;

            try { familyName = typeElem.FamilyName ?? string.Empty; } catch { familyName = string.Empty; }
            try { typeName = typeElem.Name ?? string.Empty; } catch { typeName = string.Empty; }
            return !string.IsNullOrWhiteSpace(familyName) || !string.IsNullOrWhiteSpace(typeName);
        }

        private static bool TryGetFamilyTypeSyntheticValue(Element element, ScheduleRoundtripColumn col, out string value)
        {
            value = string.Empty;
            if (element == null || col == null)
                return false;

            var paramId = col.ParamId ?? int.MinValue;
            bool wantsFamilyAndType =
                paramId == -1002052 ||
                ColumnIdentityEquals(col, "ファミリとタイプ", "Family and Type");
            bool wantsFamily =
                paramId == -1002051 ||
                ColumnIdentityEquals(col, "ファミリ", "Family");
            bool wantsType =
                paramId == -1002050 ||
                ColumnIdentityEquals(col, "タイプ", "Type");

            if (!wantsFamilyAndType && !wantsFamily && !wantsType)
                return false;

            if (!TryGetElementTypeInfo(element, out var typeElem, out var familyName, out var typeName))
                return false;

            if (wantsFamilyAndType)
            {
                if (!string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(typeName))
                    value = $"{familyName}: {typeName}";
                else
                    value = string.IsNullOrWhiteSpace(typeName) ? familyName : typeName;
                return !string.IsNullOrWhiteSpace(value);
            }

            if (wantsFamily)
            {
                value = familyName;
                return !string.IsNullOrWhiteSpace(value);
            }

            value = !string.IsNullOrWhiteSpace(typeName)
                ? typeName
                : (typeElem?.Name ?? string.Empty);
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetLevelSyntheticValue(Element element, ScheduleRoundtripColumn col, out string value)
        {
            value = string.Empty;
            if (element == null || col == null)
                return false;

            var paramId = col.ParamId ?? int.MinValue;
            if (paramId != -1002062 && !ColumnIdentityEquals(col, "レベル", "Level"))
                return false;

            ElementId? levelId = null;
            try
            {
                var lid = element.LevelId;
                if (lid != null && lid != ElementId.InvalidElementId)
                    levelId = lid;
            }
            catch { }

            if (levelId == null)
            {
                try
                {
                    var prm = element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    var lid = prm?.AsElementId();
                    if (lid != null && lid != ElementId.InvalidElementId)
                        levelId = lid;
                }
                catch { }
            }

            if (levelId == null)
                return false;

            var level = element.Document.GetElement(levelId) as Level;
            value = level?.Name ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetRoomByPhase(FamilyInstance fi, bool fromRoom, out Autodesk.Revit.DB.Architecture.Room? room)
        {
            room = null;
            if (fi == null)
                return false;

            var propName = fromRoom ? "FromRoom" : "ToRoom";
            var props = GetPublicInstancePropertiesByName(fi.GetType(), propName);
            if (props.Count == 0)
                return false;

            foreach (var prop in props.Where(x => x.GetIndexParameters().Length == 0))
            {
                try
                {
                    room = prop.GetValue(fi, null) as Autodesk.Revit.DB.Architecture.Room;
                    if (room != null)
                        return true;
                }
                catch { }
            }

            foreach (var prop in props.Where(x => x.GetIndexParameters().Length == 1))
            {
                try
                {
                    foreach (Phase phase in fi.Document.Phases)
                    {
                        var candidate = prop.GetValue(fi, new object[] { phase }) as Autodesk.Revit.DB.Architecture.Room;
                        if (candidate != null)
                            room = candidate;
                    }

                    if (room != null)
                        return true;
                }
                catch { }
            }

            return false;
        }

        private static string GetParameterExportValue(Element owner, Parameter prm, string displayText)
        {
            if (prm == null)
                return displayText ?? string.Empty;

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

                        var refElem = owner?.Document?.GetElement(refId);
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

                        return refId.IntValue().ToString(CultureInfo.InvariantCulture);
                    default:
                        return displayText ?? string.Empty;
                }
            }
            catch
            {
                return displayText ?? string.Empty;
            }
        }

        private static bool TryGetRoomSyntheticValue(Element element, ScheduleRoundtripColumn col, out string value)
        {
            value = string.Empty;
            if (element == null || col == null || !(element is FamilyInstance fi))
                return false;

            var names = GetColumnIdentityNames(col).ToList();
            if (names.Count == 0)
                return false;

            string? matchedName = null;
            bool fromRoom = false;
            foreach (var name in names)
            {
                var normalized = NormalizeKeyText(name);
                if (normalized.StartsWith(NormalizeKeyText("部屋から"), StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith("from room", StringComparison.OrdinalIgnoreCase))
                {
                    fromRoom = true;
                    matchedName = name;
                    break;
                }

                if (normalized.StartsWith(NormalizeKeyText("部屋へ"), StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith("to room", StringComparison.OrdinalIgnoreCase))
                {
                    fromRoom = false;
                    matchedName = name;
                    break;
                }
            }

            if (matchedName == null)
                return false;

            if (!TryGetRoomByPhase(fi, fromRoom, out var room) || room == null)
                return false;

            var fieldName = matchedName;
            var colonIndex = fieldName.IndexOf(':');
            if (colonIndex >= 0 && colonIndex + 1 < fieldName.Length)
                fieldName = fieldName.Substring(colonIndex + 1).Trim();

            if (string.IsNullOrWhiteSpace(fieldName))
                fieldName = "名前";

            var normalizedField = NormalizeKeyText(fieldName);
            if (string.Equals(normalizedField, NormalizeKeyText("名前"), StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedField, "name", StringComparison.OrdinalIgnoreCase))
            {
                value = NormalizeDisplayedRoomName(room.Name ?? string.Empty, room.Number ?? string.Empty);
                return !string.IsNullOrWhiteSpace(value);
            }

            if (string.Equals(normalizedField, NormalizeKeyText("番号"), StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedField, "number", StringComparison.OrdinalIgnoreCase))
            {
                value = room.Number ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            try
            {
                var payload = new JObject
                {
                    ["name"] = fieldName
                };
                var prm = ParamResolver.ResolveByPayload(room, payload, out _);
                if (prm != null)
                {
                    value = GetParameterExportValue(room, prm, string.Empty);
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
            catch { }

            return false;
        }

        private static string NormalizeDisplayedRoomName(string roomName, string roomNumber)
        {
            var name = (roomName ?? string.Empty).Trim();
            var number = (roomNumber ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            if (string.IsNullOrWhiteSpace(number))
                return name;

            var suffix = " " + number;
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - suffix.Length).TrimEnd();

            return name;
        }

        private static bool TryGetSyntheticDisplayValue(Element element, ScheduleRoundtripColumn col, out string value)
        {
            value = string.Empty;
            if (element == null || col == null)
                return false;

            if (TryGetFamilyTypeSyntheticValue(element, col, out value))
                return true;
            if (TryGetLevelSyntheticValue(element, col, out value))
                return true;
            if (TryGetRoomSyntheticValue(element, col, out value))
                return true;

            return false;
        }

        public static bool IsQuantityLikeHeader(string? header)
        {
            var normalized = NormalizeKeyText(header ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            foreach (var keyword in QuantityLikeHeaderKeywords)
            {
                if (string.Equals(normalized, NormalizeKeyText(keyword), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryParseWholeNumber(string? rawText, out int value)
        {
            value = 0;
            var text = (rawText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
            {
                value = iv;
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dv))
            {
                var rounded = (int)Math.Round(dv);
                if (Math.Abs(dv - rounded) < 0.0001d)
                {
                    value = rounded;
                    return true;
                }
            }

            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out dv))
            {
                var rounded = (int)Math.Round(dv);
                if (Math.Abs(dv - rounded) < 0.0001d)
                {
                    value = rounded;
                    return true;
                }
            }

            return false;
        }

        public static int? TryGetExpectedElementCount(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            bool sourceIsItemized)
        {
            if (displayRowValues == null || cols == null)
                return sourceIsItemized ? 1 : (int?)null;

            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                if (!IsQuantityLikeHeader(cols[i].Header))
                    continue;

                if (TryParseWholeNumber(displayRowValues[i], out var count))
                    return count;
            }

            return sourceIsItemized ? 1 : (int?)null;
        }

        public static IList<int> BuildDisplayMatchIndexes(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IList<int>? displayKeyIndexes,
            bool includeAllMeaningful)
        {
            var meaningfulIndexes = new List<int>();
            if (displayRowValues == null || cols == null)
                return meaningfulIndexes;

            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                if (IsQuantityLikeHeader(cols[i].Header))
                    continue;
                if (IsDisplayPlaceholderValue(displayRowValues[i], cols[i].Header))
                    continue;
                meaningfulIndexes.Add(i);
            }

            if (includeAllMeaningful || meaningfulIndexes.Count <= 1)
                return meaningfulIndexes;

            var preferred = meaningfulIndexes
                .Where(i =>
                    (displayKeyIndexes?.Contains(i) ?? false)
                    || IsIdentityLikeHeader(cols[i].Header)
                    || cols[i].Editable
                    || cols[i].IsBoolean)
                .Distinct()
                .ToList();

            return preferred.Count > 0 ? preferred : meaningfulIndexes;
        }

        private static bool DoesHelperRowMatchDisplayRowAtIndexes(
            IList<string> displayRowValues,
            IList<string> helperRowValues,
            IList<int> indexes)
        {
            if (displayRowValues == null || helperRowValues == null || indexes == null || indexes.Count == 0)
                return false;

            foreach (var idx in indexes)
            {
                var expected = idx < displayRowValues.Count
                    ? NormalizeKeyText(displayRowValues[idx] ?? string.Empty)
                    : string.Empty;
                var actual = idx < helperRowValues.Count
                    ? NormalizeKeyText(helperRowValues[idx] ?? string.Empty)
                    : string.Empty;

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static IList<int> TryConsumeSequentialDisplayRowElementIdsAtIndexes(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IList<(int ElementId, IList<string> Values)> helperRows,
            ref int helperCursor,
            bool sourceIsItemized,
            int? expectedCount,
            IList<int> indexes)
        {
            if (displayRowValues == null || cols == null || helperRows == null || indexes == null || indexes.Count == 0)
                return Array.Empty<int>();

            if (helperCursor < 0)
                helperCursor = 0;
            if (helperCursor >= helperRows.Count)
                return Array.Empty<int>();
            if (expectedCount.HasValue && expectedCount.Value <= 0)
                return Array.Empty<int>();

            if (!DoesHelperRowMatchDisplayRowAtIndexes(displayRowValues, helperRows[helperCursor].Values, indexes))
            {
                if (!sourceIsItemized && expectedCount.HasValue)
                {
                    var fallbackCount = expectedCount.Value;
                    if (helperCursor + fallbackCount <= helperRows.Count)
                    {
                        var fallbackIds = helperRows
                            .Skip(helperCursor)
                            .Take(fallbackCount)
                            .Select(x => x.ElementId)
                            .Where(x => x > 0)
                            .Distinct()
                            .ToList();
                        if (fallbackIds.Count == fallbackCount)
                        {
                            helperCursor += fallbackCount;
                            return fallbackIds;
                        }
                    }
                }

                return Array.Empty<int>();
            }

            var runEnd = helperCursor;
            while (runEnd < helperRows.Count
                   && DoesHelperRowMatchDisplayRowAtIndexes(displayRowValues, helperRows[runEnd].Values, indexes))
            {
                runEnd++;
            }

            var runLength = runEnd - helperCursor;
            if (runLength <= 0)
                return Array.Empty<int>();

            if (expectedCount.HasValue)
            {
                if (runLength < expectedCount.Value)
                {
                    if (!sourceIsItemized)
                    {
                        var fallbackCount = expectedCount.Value;
                        if (helperCursor + fallbackCount <= helperRows.Count)
                        {
                            var fallbackIds = helperRows
                                .Skip(helperCursor)
                                .Take(fallbackCount)
                                .Select(x => x.ElementId)
                                .Where(x => x > 0)
                                .Distinct()
                                .ToList();
                            if (fallbackIds.Count == fallbackCount)
                            {
                                helperCursor += fallbackCount;
                                return fallbackIds;
                            }
                        }
                    }

                    return Array.Empty<int>();
                }

                var count = expectedCount.Value;
                var ids = helperRows
                    .Skip(helperCursor)
                    .Take(count)
                    .Select(x => x.ElementId)
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (ids.Count != count)
                    return Array.Empty<int>();

                helperCursor += count;
                return ids;
            }

            if (sourceIsItemized || runLength == 1)
            {
                var id = helperRows[helperCursor].ElementId;
                helperCursor++;
                return id > 0 ? new[] { id } : Array.Empty<int>();
            }

            var runIds = helperRows
                .Skip(helperCursor)
                .Take(runLength)
                .Select(x => x.ElementId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            if (runIds.Count <= 0)
                return Array.Empty<int>();

            helperCursor += runLength;
            return runIds;
        }

        public static IList<int> ConsumeSequentialDisplayRowElementIds(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IList<(int ElementId, IList<string> Values)> helperRows,
            ref int helperCursor,
            bool sourceIsItemized,
            IList<int>? displayKeyIndexes = null)
        {
            if (displayRowValues == null || cols == null || helperRows == null || helperRows.Count == 0)
                return Array.Empty<int>();
            if (IsDisplayHeaderLikeRow(displayRowValues, cols))
                return Array.Empty<int>();
            if (CountMeaningfulDisplayValues(displayRowValues, cols) <= 0)
                return Array.Empty<int>();
            if (IsLikelyDisplayNonElementRow(displayRowValues, cols, displayKeyIndexes ?? Array.Empty<int>()))
                return Array.Empty<int>();

            var expectedCount = TryGetExpectedElementCount(displayRowValues, cols, sourceIsItemized);
            var primaryIndexes = BuildDisplayMatchIndexes(displayRowValues, cols, displayKeyIndexes, includeAllMeaningful: false);
            var strictIndexes = BuildDisplayMatchIndexes(displayRowValues, cols, displayKeyIndexes, includeAllMeaningful: true);
            var indexCandidates = (!expectedCount.HasValue && !sourceIsItemized)
                ? new[] { strictIndexes }
                : new[] { primaryIndexes, strictIndexes };

            foreach (var indexes in indexCandidates)
            {
                var ids = TryConsumeSequentialDisplayRowElementIdsAtIndexes(
                    displayRowValues,
                    cols,
                    helperRows,
                    ref helperCursor,
                    sourceIsItemized,
                    expectedCount,
                    indexes);
                if (ids.Count > 0)
                    return ids;
            }

            return Array.Empty<int>();
        }

        public static void TryAdvanceHelperCursor(
            IList<(int ElementId, IList<string> Values)> helperRows,
            ref int helperCursor,
            IEnumerable<int>? mappedIds)
        {
            if (helperRows == null || mappedIds == null)
                return;
            if (helperCursor < 0 || helperCursor >= helperRows.Count)
                return;

            var ids = mappedIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            if (ids.Count <= 0)
                return;
            if (helperCursor + ids.Count > helperRows.Count)
                return;

            var helperBlock = helperRows
                .Skip(helperCursor)
                .Take(ids.Count)
                .Select(x => x.ElementId)
                .Where(x => x > 0)
                .ToList();

            if (helperBlock.Count == ids.Count
                && new HashSet<int>(helperBlock).SetEquals(ids))
            {
                helperCursor += ids.Count;
            }
        }

        public static IDictionary<int, List<int>> BuildSequentialBaselineRowElementMap(
            IXLWorksheet baselineSheet,
            IList<ScheduleRoundtripColumn> displayCols,
            IDictionary<string, int> baselineHeaderColumnMap,
            IList<(int ElementId, IList<string> Values)> helperRows,
            bool sourceIsItemized,
            IList<int>? displayKeyIndexes = null)
        {
            var map = new Dictionary<int, List<int>>();
            if (baselineSheet == null || displayCols == null || displayCols.Count == 0 || helperRows == null || helperRows.Count == 0)
                return map;

            int helperCursor = 0;
            int lastRow = baselineSheet.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                var rowValues = ReadWorksheetDisplayRowValues(baselineSheet, row, displayCols, baselineHeaderColumnMap);
                var ids = ConsumeSequentialDisplayRowElementIds(
                    rowValues,
                    displayCols,
                    helperRows,
                    ref helperCursor,
                    sourceIsItemized,
                    displayKeyIndexes);

                if (ids.Count > 0)
                    map[row] = ids.Distinct().OrderBy(x => x).ToList();
            }

            return map;
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
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            foreach (var keyword in IdentityLikeHeaderKeywords)
            {
                if (normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        public static int CountMeaningfulDisplayValues(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols)
        {
            if (displayRowValues == null || cols == null)
                return 0;

            int count = 0;
            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                if (!IsDisplayPlaceholderValue(displayRowValues[i], cols[i].Header))
                    count++;
            }

            return count;
        }

        public static bool IsDisplayHeaderLikeRow(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols)
        {
            if (displayRowValues == null || cols == null)
                return false;

            int comparableCount = 0;
            for (int i = 0; i < displayRowValues.Count && i < cols.Count; i++)
            {
                var value = NormalizeKeyText(displayRowValues[i] ?? string.Empty);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                comparableCount++;
                var header = NormalizeKeyText(cols[i].Header ?? string.Empty);
                if (!string.Equals(value, header, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (comparableCount <= 0)
                return false;

            return comparableCount >= Math.Min(2, Math.Max(1, cols.Count));
        }

        public static IList<int> MatchDisplayRowToLiveElementIds(
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols,
            IDictionary<string, List<int>> identityValueElementMap,
            IList<(int ElementId, IList<string> Values)> liveElementRows)
        {
            if (displayRowValues == null || cols == null || liveElementRows == null)
                return Array.Empty<int>();

            var identityMatches = MatchDisplayRowToElementIdsByIdentityValueMap(
                    displayRowValues,
                    cols,
                    identityValueElementMap)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            if (identityMatches.Count > 0)
            {
                var allowedElementIds = new HashSet<int>(liveElementRows.Select(x => x.ElementId));
                identityMatches = identityMatches
                    .Where(allowedElementIds.Contains)
                    .ToList();
            }
            if (identityMatches.Count == 1)
                return identityMatches;

            int meaningfulValueCount = CountMeaningfulDisplayValues(displayRowValues, cols);
            if (identityMatches.Count > 1)
            {
                var narrowedRows = liveElementRows
                    .Where(x => identityMatches.Contains(x.ElementId))
                    .ToList();
                var narrowedMatches = SelectiveMatchDisplayRowToElementIds(
                        displayRowValues,
                        cols,
                        narrowedRows)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
                if (narrowedMatches.Count > 0)
                    return narrowedMatches;

                if (meaningfulValueCount < 2)
                    return Array.Empty<int>();

                var distinctVisibleKeys = narrowedRows
                    .Select(x => BuildVisibleRowKey(x.Values))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (distinctVisibleKeys == 1)
                    return identityMatches;

                return Array.Empty<int>();
            }

            if (meaningfulValueCount < 2)
                return Array.Empty<int>();

            return SelectiveMatchDisplayRowToElementIds(displayRowValues, cols, liveElementRows)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        public static bool DoesElementMatchDisplayIdentityValues(
            Document doc,
            int elementId,
            IList<string> displayRowValues,
            IList<ScheduleRoundtripColumn> cols)
        {
            if (doc == null || elementId <= 0 || displayRowValues == null || cols == null || cols.Count == 0)
                return false;

            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            if (element == null)
                return false;

            var identityIndexes = Enumerable.Range(0, Math.Min(cols.Count, displayRowValues.Count))
                .Where(i =>
                    IsIdentityLikeHeader(cols[i].Header)
                    && !IsDisplayPlaceholderValue(displayRowValues[i], cols[i].Header))
                .ToList();

            if (identityIndexes.Count == 0)
                return true;

            foreach (var idx in identityIndexes)
            {
                var expected = NormalizeKeyText(displayRowValues[idx] ?? string.Empty);
                var actual = NormalizeKeyText(GetExportValueForColumn(element, cols[idx], string.Empty));
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
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

            selectedIndexes = selectedIndexes
                .Where(i => itemizedRows.Any(row =>
                    i < row.Values.Count
                    && !IsDisplayPlaceholderValue(row.Values[i], cols[i].Header)))
                .ToList();

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

        public static bool IsLikelyDisplayNonElementRow(
            IList<string> values,
            IList<ScheduleRoundtripColumn> cols,
            IList<int> keyIndexes)
        {
            if (values == null || cols == null)
                return true;

            int meaningful = CountMeaningfulDisplayValues(values, cols);
            if (meaningful <= 0)
                return true;

            if (meaningful == 1)
                return true;

            if (keyIndexes != null
                && keyIndexes.Count > 0
                && HasOnlyBlankNonKeyValues(values, keyIndexes))
                return true;

            var normalizedHeaders = new HashSet<string>(
                (cols ?? Array.Empty<ScheduleRoundtripColumn>())
                    .Select(x => NormalizeKeyText(x?.Header ?? string.Empty))
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            int headerLikeValueCount = 0;
            for (int i = 0; i < values.Count && i < cols.Count; i++)
            {
                if (IsDisplayPlaceholderValue(values[i], cols[i].Header))
                    continue;

                var normalizedValue = NormalizeKeyText(values[i] ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalizedValue))
                    continue;

                if (normalizedHeaders.Contains(normalizedValue))
                    headerLikeValueCount++;
            }

            if (meaningful >= 2 && headerLikeValueCount >= Math.Max(2, meaningful - 1))
                return true;

            return false;
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

        public static bool ValidateWorkbookLayout(
            IXLWorksheet? metaSheet,
            IXLWorksheet dataSheet,
            IXLWorksheet baselineSheet,
            IList<ScheduleRoundtripColumn> allCols,
            out string message)
        {
            message = string.Empty;
            if (dataSheet == null || baselineSheet == null)
            {
                message = "Workbook structure is invalid.";
                return false;
            }

            bool authoritativeRowToken = IsRowTokenAuthoritativeWorkbook(metaSheet);

            if (!string.Equals(
                NormalizeKeyText(dataSheet.Cell(1, 1).GetString()),
                NormalizeKeyText(HiddenIdHeader),
                StringComparison.OrdinalIgnoreCase))
            {
                message = "Workbook layout changed. Column A must remain __ElementId.";
                return false;
            }

            int dataRowTokenCol = FindWorksheetColumnByHeader(dataSheet, HiddenRowTokenHeader);
            int baselineRowTokenCol = FindWorksheetColumnByHeader(baselineSheet, HiddenRowTokenHeader);
            if (authoritativeRowToken)
            {
                if (dataRowTokenCol <= 0)
                {
                    message = "Workbook layout changed. Hidden column __RowToken is missing.";
                    return false;
                }

                if (baselineRowTokenCol <= 0)
                {
                    message = "Baseline snapshot is missing __RowToken.";
                    return false;
                }
            }

            int dataRowIdentityCol = FindWorksheetColumnByHeader(dataSheet, HiddenRowIdentityHeader);
            if (dataRowIdentityCol <= 0)
            {
                message = "Workbook layout changed. Hidden column __RowIdentity is missing.";
                return false;
            }

            int baselineRowIdentityCol = FindWorksheetColumnByHeader(baselineSheet, HiddenRowIdentityHeader);
            if (baselineRowIdentityCol <= 0)
            {
                message = "Baseline snapshot is missing __RowIdentity.";
                return false;
            }

            foreach (var col in (allCols ?? Array.Empty<ScheduleRoundtripColumn>()).OrderBy(x => x.OutputColumnNumber))
            {
                var actualHeader = NormalizeKeyText(dataSheet.Cell(1, col.OutputColumnNumber).GetString());
                var expectedHeader = NormalizeKeyText(baselineSheet.Cell(1, col.OutputColumnNumber).GetString());
                if (!string.Equals(actualHeader, expectedHeader, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"Workbook layout changed around column {col.OutputColumnNumber}. Do not reorder columns.";
                    return false;
                }
            }

            var seenRowTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRowIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lastRow = dataSheet.LastRowUsed()?.RowNumber() ?? 1;
            for (int row = 2; row <= lastRow; row++)
            {
                if (authoritativeRowToken)
                {
                    var rowToken = NormalizeRowToken(dataSheet.Cell(row, dataRowTokenCol).GetString());
                    if (string.IsNullOrWhiteSpace(rowToken))
                    {
                        message = $"Workbook row token is missing at row {row}. Do not delete hidden columns.";
                        return false;
                    }

                    if (!seenRowTokens.Add(rowToken))
                    {
                        message = $"Workbook row token is duplicated at row {row}. Do not duplicate or copy/paste rows partially.";
                        return false;
                    }
                }

                var rowIdentity = NormalizeKeyText(dataSheet.Cell(row, dataRowIdentityCol).GetString());
                if (string.IsNullOrWhiteSpace(rowIdentity))
                    continue;
                if (seenRowIdentities.Add(rowIdentity))
                    continue;

                message = $"Workbook row identity is duplicated at row {row}. Do not duplicate or copy/paste rows partially.";
                return false;
            }

            return true;
        }

        public static IDictionary<string, int> BuildBaselineRowTokenLookup(IList<ScheduleBaselineRow> baselineRows)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (baselineRows == null)
                return map;

            foreach (var baselineRow in baselineRows)
            {
                var rowToken = NormalizeRowToken(baselineRow.RowToken);
                if (string.IsNullOrWhiteSpace(rowToken) || map.ContainsKey(rowToken))
                    continue;

                map[rowToken] = baselineRow.RowNumber;
            }

            return map;
        }

        public static string GetExportValueForColumn(Element element, ScheduleRoundtripColumn col, string displayText)
        {
            if (col.IsBoolean)
            {
                if (element != null)
                {
                    var booleanParam = ResolveParameterByFixedScope(element, col, out _, out _);
                    if (booleanParam != null)
                        return booleanParam.AsInteger() != 0 ? "☑" : "☐";
                }

                if (TryParseDisplayedBooleanText(displayText, out var displayBool))
                    return displayBool ? "☑" : "☐";
            }

            if (element == null)
                return displayText ?? string.Empty;

            // Some schedule display fields (family/type, level, from/to room) do not round-trip
            // through plain parameter resolution with the same text Revit shows in schedules.
            // Prefer the known synthetic reconstruction first so helper-row/live matching stays
            // aligned with visible schedule text.
            if (TryGetSyntheticDisplayValue(element, col, out var synthetic))
                return synthetic;

            var prm = ResolveParameterByFixedScope(element, col, out _, out _);
            if (prm == null)
                return displayText ?? string.Empty;

            return GetParameterExportValue(element, prm, displayText);
        }

        public static bool ExportValuesEqual(string? left, string? right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        public static string BuildWorksheetRowLabel(
            IXLWorksheet ws,
            int rowNumber,
            IList<ScheduleRoundtripColumn> cols,
            int maxParts = 3)
        {
            var parts = new List<string>();
            foreach (var col in cols.OrderBy(x => x.OutputColumnNumber))
            {
                var value = (ws.Cell(rowNumber, col.OutputColumnNumber).GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                parts.Add(value);
                if (parts.Count >= maxParts)
                    break;
            }

            return parts.Count == 0 ? $"row-{rowNumber}" : string.Join(" / ", parts);
        }

        public static void WriteMetaSheet(
            IXLWorksheet meta,
            Document doc,
            ViewSchedule schedule,
            IList<ScheduleRoundtripColumn> cols,
            string exportSessionId)
        {
            meta.Cell(1, 1).Value = "docTitle";
            meta.Cell(1, 2).Value = doc.Title;
            meta.Cell(1, 4).Value = "schemaVersion";
            meta.Cell(1, 5).Value = SchemaVersionV2;
            meta.Cell(2, 1).Value = "scheduleViewId";
            meta.Cell(2, 2).Value = schedule.Id.IntValue();
            meta.Cell(2, 4).Value = "rowResolutionMode";
            meta.Cell(2, 5).Value = RowResolutionModeAuthoritative;
            meta.Cell(3, 1).Value = "scheduleName";
            meta.Cell(3, 2).Value = schedule.Name;
            meta.Cell(3, 4).Value = "exportSessionId";
            meta.Cell(3, 5).Value = exportSessionId ?? string.Empty;
            meta.Cell(4, 1).Value = "exportMode";
            meta.Cell(4, 2).Value = ExportModeRoundtrip;
            meta.Cell(4, 4).Value = "docGuid";
            meta.Cell(4, 5).Value = GetCurrentDocGuid(doc);

            meta.Cell(5, 1).Value = "outputColumn";
            meta.Cell(5, 2).Value = "header";
            meta.Cell(5, 3).Value = "paramName";
            meta.Cell(5, 4).Value = "paramId";
            meta.Cell(5, 5).Value = "storageType";
            meta.Cell(5, 6).Value = "dataTypeId";
            meta.Cell(5, 7).Value = "editable";
            meta.Cell(5, 8).Value = "isBoolean";
            meta.Cell(5, 9).Value = "sourceFieldName";
            meta.Cell(5, 10).Value = "resolvedScope";

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
                meta.Cell(row, 10).Value = NormalizeResolvedScope(col.ResolvedScope);
                row++;
            }

            meta.Columns().AdjustToContents();
            meta.Hide();
        }

        public static ScheduleRowMapEntry BuildRowMapEntry(
            Document doc,
            int worksheetRowNumber,
            string rowToken,
            string rowIdentity,
            IList<int>? elementIds,
            IList<string>? visibleValues,
            IList<ScheduleRoundtripColumn> cols,
            bool meaningfulRow)
        {
            var normalizedElementIds = (elementIds ?? Array.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            return new ScheduleRowMapEntry
            {
                WorksheetRowNumber = worksheetRowNumber,
                RowToken = NormalizeRowToken(rowToken),
                RowIdentity = rowIdentity ?? string.Empty,
                ElementIds = normalizedElementIds,
                TypeIds = GetTypeIdsForElementIds(doc, normalizedElementIds).ToList(),
                Scope = ResolveRowScope(cols),
                GroupCount = normalizedElementIds.Count,
                MeaningfulRow = meaningfulRow,
                VisibleRowFingerprint = BuildVisibleRowKey(visibleValues ?? Array.Empty<string>())
            };
        }

        public static void WriteRowMapSheet(
            IXLWorksheet rowMapSheet,
            IDictionary<int, List<int>> rowMap,
            IDictionary<int, ScheduleRowMapEntry>? rowMetadata,
            string exportMode)
        {
            rowMapSheet.Cell(1, 1).Value = "exportMode";
            rowMapSheet.Cell(1, 2).Value = exportMode;
            rowMapSheet.Cell(3, 1).Value = "row";
            rowMapSheet.Cell(3, 2).Value = "elementIds";
            rowMapSheet.Cell(3, 3).Value = "rowToken";
            rowMapSheet.Cell(3, 4).Value = "typeIds";
            rowMapSheet.Cell(3, 5).Value = "scope";
            rowMapSheet.Cell(3, 6).Value = "groupCount";
            rowMapSheet.Cell(3, 7).Value = "meaningfulRow";
            rowMapSheet.Cell(3, 8).Value = "rowIdentity";
            rowMapSheet.Cell(3, 9).Value = "visibleRowFingerprint";

            int row = 4;
            foreach (var kv in rowMap.OrderBy(x => x.Key))
            {
                rowMapSheet.Cell(row, 1).Value = kv.Key;
                rowMapSheet.Cell(row, 2).Value = string.Join(";", kv.Value ?? new List<int>());
                if (rowMetadata != null && rowMetadata.TryGetValue(kv.Key, out var entry) && entry != null)
                {
                    rowMapSheet.Cell(row, 3).Value = entry.RowToken;
                    rowMapSheet.Cell(row, 4).Value = string.Join(";", entry.TypeIds ?? new List<int>());
                    rowMapSheet.Cell(row, 5).Value = entry.Scope;
                    rowMapSheet.Cell(row, 6).Value = entry.GroupCount;
                    rowMapSheet.Cell(row, 7).Value = entry.MeaningfulRow ? "true" : "false";
                    rowMapSheet.Cell(row, 8).Value = entry.RowIdentity;
                    rowMapSheet.Cell(row, 9).Value = entry.VisibleRowFingerprint;
                }
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

        public static IDictionary<string, ScheduleRowMapEntry> ReadRowMapEntriesByToken(IXLWorksheet? rowMapSheet)
        {
            var map = new Dictionary<string, ScheduleRowMapEntry>(StringComparer.OrdinalIgnoreCase);
            if (rowMapSheet == null)
                return map;

            int row = 4;
            while (!rowMapSheet.Cell(row, 1).IsEmpty())
            {
                var rowNoText = rowMapSheet.Cell(row, 1).GetString();
                if (!int.TryParse(rowNoText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNo) || rowNo <= 0)
                {
                    row++;
                    continue;
                }

                var rowToken = NormalizeRowToken(rowMapSheet.Cell(row, 3).GetString());
                if (string.IsNullOrWhiteSpace(rowToken))
                {
                    row++;
                    continue;
                }

                if (!map.ContainsKey(rowToken))
                {
                    map[rowToken] = new ScheduleRowMapEntry
                    {
                        WorksheetRowNumber = rowNo,
                        RowToken = rowToken,
                        ElementIds = ParseElementIds(rowMapSheet.Cell(row, 2).GetString()).ToList(),
                        TypeIds = ParseElementIds(rowMapSheet.Cell(row, 4).GetString()).ToList(),
                        Scope = NormalizeResolvedScope(rowMapSheet.Cell(row, 5).GetString()),
                        GroupCount = SafeInt(rowMapSheet.Cell(row, 6).GetString()),
                        MeaningfulRow = ParseBool(rowMapSheet.Cell(row, 7).GetString()),
                        RowIdentity = rowMapSheet.Cell(row, 8).GetString(),
                        VisibleRowFingerprint = rowMapSheet.Cell(row, 9).GetString()
                    };
                }

                row++;
            }

            return map;
        }

        public static int FindWorksheetColumnByHeader(IXLWorksheet ws, string header)
        {
            if (ws == null || string.IsNullOrWhiteSpace(header))
                return 0;

            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                if (string.Equals(
                    NormalizeKeyText(ws.Cell(1, col).GetString()),
                    NormalizeKeyText(header),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return col;
                }
            }

            return 0;
        }

        public static string GetWorksheetRowToken(IXLWorksheet ws, int rowNumber)
        {
            if (ws == null || rowNumber <= 0)
                return string.Empty;

            int rowTokenCol = FindWorksheetColumnByHeader(ws, HiddenRowTokenHeader);
            if (rowTokenCol <= 0)
                return string.Empty;

            return NormalizeRowToken(ws.Cell(rowNumber, rowTokenCol).GetString());
        }

        public static bool ValidateWorkbookDocumentScope(Document doc, IXLWorksheet? meta, out string message)
        {
            message = string.Empty;
            var expectedDocGuid = GetWorkbookDocGuid(meta);
            if (string.IsNullOrWhiteSpace(expectedDocGuid))
                return true;

            var currentDocGuid = GetCurrentDocGuid(doc);
            if (string.IsNullOrWhiteSpace(currentDocGuid))
                return true;

            if (string.Equals(expectedDocGuid, currentDocGuid, StringComparison.OrdinalIgnoreCase))
                return true;

            message = "Workbook target document does not match the current Revit project.";
            return false;
        }

        public static void WriteBaselineSheet(IXLWorksheet baselineSheet, IXLWorksheet sourceSheet, int lastRow, int lastCol)
        {
            for (int row = 1; row <= lastRow; row++)
            {
                for (int col = 1; col <= lastCol; col++)
                    baselineSheet.Cell(row, col).Value = sourceSheet.Cell(row, col).GetString();
            }

            baselineSheet.Hide();
        }

        public static IList<ScheduleBaselineRow> BuildBaselineRows(IXLWorksheet baselineSheet, IList<ScheduleRoundtripColumn> cols)
        {
            var rows = new List<ScheduleBaselineRow>();
            int lastRow = baselineSheet.LastRowUsed()?.RowNumber() ?? 1;
            int rowTokenCol = FindWorksheetColumnByHeader(baselineSheet, HiddenRowTokenHeader);
            int rowIdentityCol = FindWorksheetColumnByHeader(baselineSheet, HiddenRowIdentityHeader);
            for (int row = 2; row <= lastRow; row++)
            {
                rows.Add(new ScheduleBaselineRow
                {
                    RowNumber = row,
                    RowToken = rowTokenCol > 0
                        ? NormalizeRowToken(baselineSheet.Cell(row, rowTokenCol).GetString())
                        : string.Empty,
                    RowIdentity = rowIdentityCol > 0
                        ? NormalizeKeyText(baselineSheet.Cell(row, rowIdentityCol).GetString())
                        : string.Empty,
                    HiddenIdKey = NormalizeWorksheetRowIdentityKey(baselineSheet.Cell(row, 1).GetString()),
                    Values = ReadWorksheetRowValuesByColumns(baselineSheet, row, cols)
                });
            }

            return rows;
        }

        public static IDictionary<string, int> BuildBaselineRowIdentityLookup(IList<ScheduleBaselineRow> baselineRows)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (baselineRows == null)
                return map;

            foreach (var baselineRow in baselineRows)
            {
                if (string.IsNullOrWhiteSpace(baselineRow.RowIdentity) || map.ContainsKey(baselineRow.RowIdentity))
                    continue;
                map[baselineRow.RowIdentity] = baselineRow.RowNumber;
            }

            return map;
        }

        public static IDictionary<string, List<int>> BuildBaselineHiddenIdLookup(IList<ScheduleBaselineRow> baselineRows)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            if (baselineRows == null)
                return map;

            foreach (var baselineRow in baselineRows)
            {
                if (string.IsNullOrWhiteSpace(baselineRow.HiddenIdKey))
                    continue;

                if (!map.TryGetValue(baselineRow.HiddenIdKey, out var rows))
                {
                    rows = new List<int>();
                    map[baselineRow.HiddenIdKey] = rows;
                }

                if (!rows.Contains(baselineRow.RowNumber))
                    rows.Add(baselineRow.RowNumber);
            }

            return map;
        }

        public static IDictionary<string, List<int>> BuildBaselineVisibleRowLookup(IList<ScheduleBaselineRow> baselineRows)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            if (baselineRows == null)
                return map;

            foreach (var baselineRow in baselineRows)
            {
                var key = BuildVisibleRowKey(baselineRow.Values);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!map.TryGetValue(key, out var rows))
                {
                    rows = new List<int>();
                    map[key] = rows;
                }

                if (!rows.Contains(baselineRow.RowNumber))
                    rows.Add(baselineRow.RowNumber);
            }

            return map;
        }

        public static ScheduleBaselineRowResolution ResolveBaselineRow(
            IXLWorksheet dataSheet,
            IList<ScheduleRoundtripColumn> cols,
            IList<ScheduleBaselineRow> baselineRows,
            IDictionary<string, int> baselineRowTokenLookup,
            IDictionary<string, int> baselineRowIdentityLookup,
            IDictionary<string, List<int>> baselineHiddenIdLookup,
            IDictionary<string, List<int>> baselineVisibleRowLookup,
            int row,
            bool allowVisibleFallback = true)
        {
            var resolution = new ScheduleBaselineRowResolution
            {
                RowNumber = row,
                Source = "row-number"
            };

            if (baselineRows == null || baselineRows.Count == 0)
                return resolution;

            int rowTokenCol = FindWorksheetColumnByHeader(dataSheet, HiddenRowTokenHeader);
            if (rowTokenCol > 0)
            {
                var rowToken = NormalizeRowToken(dataSheet.Cell(row, rowTokenCol).GetString());
                if (!string.IsNullOrWhiteSpace(rowToken)
                    && baselineRowTokenLookup != null
                    && baselineRowTokenLookup.TryGetValue(rowToken, out var baselineRowByToken)
                    && baselineRowByToken > 0)
                {
                    resolution.RowNumber = baselineRowByToken;
                    resolution.Source = "row-token";
                    return resolution;
                }
            }

            int rowIdentityCol = FindWorksheetColumnByHeader(dataSheet, HiddenRowIdentityHeader);
            if (rowIdentityCol > 0)
            {
                var rowIdentity = NormalizeKeyText(dataSheet.Cell(row, rowIdentityCol).GetString());
                if (!string.IsNullOrWhiteSpace(rowIdentity)
                    && baselineRowIdentityLookup != null
                    && baselineRowIdentityLookup.TryGetValue(rowIdentity, out var baselineRowByIdentity)
                    && baselineRowByIdentity > 0)
                {
                    resolution.RowNumber = baselineRowByIdentity;
                    resolution.Source = "row-identity";
                    return resolution;
                }
            }

            var hiddenIdKey = NormalizeWorksheetRowIdentityKey(dataSheet.Cell(row, 1).GetString());
            if (!string.IsNullOrWhiteSpace(hiddenIdKey)
                && baselineHiddenIdLookup != null
                && baselineHiddenIdLookup.TryGetValue(hiddenIdKey, out var baselineRowsByHiddenId)
                && baselineRowsByHiddenId.Count == 1)
            {
                resolution.RowNumber = baselineRowsByHiddenId[0];
                resolution.Source = "hidden-id";
                return resolution;
            }

            if (!allowVisibleFallback)
                return resolution;

            var currentValues = ReadWorksheetRowValuesByColumns(dataSheet, row, cols);
            var visibleKey = BuildVisibleRowKey(currentValues);
            if (!string.IsNullOrWhiteSpace(visibleKey)
                && baselineVisibleRowLookup != null
                && baselineVisibleRowLookup.TryGetValue(visibleKey, out var exactRows)
                && exactRows.Count == 1)
            {
                resolution.RowNumber = exactRows[0];
                resolution.Source = "exact-visible-values";
                return resolution;
            }

            int bestScore = -1;
            int secondBestScore = -1;
            int bestRow = row;
            foreach (var baselineRow in baselineRows)
            {
                var score = ScoreVisibleRowMatch(currentValues, baselineRow.Values);
                if (score > bestScore)
                {
                    secondBestScore = bestScore;
                    bestScore = score;
                    bestRow = baselineRow.RowNumber;
                }
                else if (score > secondBestScore)
                {
                    secondBestScore = score;
                }
            }

            if (bestScore > 0
                && bestScore >= Math.Max(1, currentValues.Count - 2)
                && bestScore > secondBestScore)
            {
                resolution.RowNumber = bestRow;
                resolution.Source = "fuzzy-visible-values";
            }

            return resolution;
        }

        private static string NormalizeWorksheetRowIdentityKey(string raw)
        {
            var ids = ParseElementIds(raw);
            if (ids.Count > 0)
                return string.Join(";", ids.Distinct().OrderBy(x => x));
            return (raw ?? string.Empty).Trim();
        }

        public static IList<string> ReadWorksheetRowValuesByColumns(IXLWorksheet ws, int row, IList<ScheduleRoundtripColumn> cols)
        {
            var values = new List<string>();
            if (ws == null || cols == null)
                return values;

            foreach (var col in cols.OrderBy(x => x.OutputColumnNumber))
                values.Add(ws.Cell(row, col.OutputColumnNumber).GetString());

            return values;
        }

        public static string BuildVisibleRowKey(IList<string> values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            return string.Join("\u001F", values.Select(v => NormalizeKeyText(v ?? string.Empty)));
        }

        public static int ScoreVisibleRowMatch(IList<string> currentValues, IList<string> baselineValues)
        {
            int count = Math.Min(currentValues?.Count ?? 0, baselineValues?.Count ?? 0);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                if (ExportValuesEqual(currentValues[i], baselineValues[i]))
                    score++;
            }

            return score;
        }

        public static string BuildWorksheetRowIdentity(int exportRowNumber, IEnumerable<int>? elementIds)
        {
            var normalizedIds = string.Join("-",
                (elementIds ?? Array.Empty<int>())
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(6)
                    .Select(x => x.ToString(CultureInfo.InvariantCulture)));

            if (string.IsNullOrWhiteSpace(normalizedIds))
                normalizedIds = "none";

            return $"RID-{exportRowNumber.ToString("D6", CultureInfo.InvariantCulture)}-{normalizedIds}";
        }

        public static string BuildRowToken(string exportSessionId, int exportRowNumber)
        {
            var session = (exportSessionId ?? string.Empty).Trim();
            if (session.Length > 8)
                session = session.Substring(0, 8);

            var rand = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"rt_{session}_{exportRowNumber.ToString("D6", CultureInfo.InvariantCulture)}_{rand}";
        }

        public static string BuildExportSessionId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string NormalizeRowToken(string? raw)
        {
            return (raw ?? string.Empty).Trim();
        }

        public static string GetWorkbookSchemaVersion(IXLWorksheet? meta)
        {
            if (meta == null)
                return string.Empty;

            return (meta.Cell(1, 5).GetString() ?? string.Empty).Trim();
        }

        public static string GetWorkbookRowResolutionMode(IXLWorksheet? meta)
        {
            if (meta == null)
                return string.Empty;

            return (meta.Cell(2, 5).GetString() ?? string.Empty).Trim();
        }

        public static string GetWorkbookDocGuid(IXLWorksheet? meta)
        {
            if (meta == null)
                return string.Empty;

            return (meta.Cell(4, 5).GetString() ?? string.Empty).Trim();
        }

        public static bool IsRowTokenAuthoritativeWorkbook(IXLWorksheet? meta)
        {
            return string.Equals(
                GetWorkbookSchemaVersion(meta),
                SchemaVersionV2,
                StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    GetWorkbookRowResolutionMode(meta),
                    RowResolutionModeAuthoritative,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCurrentDocGuid(Document? doc)
        {
            if (doc == null)
                return string.Empty;

            try
            {
                return (DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _) ?? string.Empty).Trim();
            }
            catch
            {
                try { return (doc.ProjectInformation?.UniqueId ?? string.Empty).Trim(); } catch { return string.Empty; }
            }
        }

        public static IList<int> GetTypeIdsForElementIds(Document doc, IEnumerable<int>? elementIds)
        {
            var ids = new HashSet<int>();
            foreach (var eid in elementIds ?? Array.Empty<int>())
            {
                if (eid <= 0)
                    continue;

                try
                {
                    var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                    var typeId = element?.GetTypeId();
                    if (typeId == null || typeId == ElementId.InvalidElementId)
                        continue;

                    var intValue = typeId.IntValue();
                    if (intValue > 0)
                        ids.Add(intValue);
                }
                catch { }
            }

            return ids.OrderBy(x => x).ToList();
        }

        public static string ResolveRowScope(IList<ScheduleRoundtripColumn> cols)
        {
            bool hasEditableInstance = false;
            bool hasEditableType = false;

            foreach (var col in cols ?? Array.Empty<ScheduleRoundtripColumn>())
            {
                if (col == null || !col.Editable)
                    continue;

                if (IsTypeLikeScope(col.ResolvedScope))
                    hasEditableType = true;
                else
                    hasEditableInstance = true;
            }

            if (hasEditableInstance && hasEditableType)
                return "mixed";
            if (hasEditableType)
                return "type";
            if (hasEditableInstance)
                return "instance";
            return string.Empty;
        }

        public static IList<int> ResolveWorksheetRowElementIds(
            IXLWorksheet dataSheet,
            IXLWorksheet baselineSheet,
            IDictionary<int, List<int>> rowMap,
            int row,
            ScheduleBaselineRowResolution baselineResolution)
        {
            bool preferBaseline = true;
            int baselineRow = Math.Max(1, baselineResolution?.RowNumber ?? row);

            List<int>? candidate = null;

            if (!preferBaseline)
            {
                candidate = ParseElementIds(dataSheet.Cell(row, 1).GetString()).ToList();
                if (candidate.Count > 0)
                    return candidate;

                if (rowMap.TryGetValue(row, out var currentMappedIds) && currentMappedIds.Count > 0)
                    return currentMappedIds.ToList();
            }

            candidate = ParseElementIds(baselineSheet.Cell(baselineRow, 1).GetString()).ToList();
            if (candidate.Count > 0)
                return candidate;

            if (rowMap.TryGetValue(baselineRow, out var baselineMappedIds) && baselineMappedIds.Count > 0)
                return baselineMappedIds.ToList();

            candidate = ParseElementIds(dataSheet.Cell(row, 1).GetString()).ToList();
            if (candidate.Count > 0)
                return candidate;

            if (rowMap.TryGetValue(row, out var fallbackMappedIds) && fallbackMappedIds.Count > 0)
                return fallbackMappedIds.ToList();

            return Array.Empty<int>();
        }

        public static bool TryGetWorksheetRowMapEntryByToken(
            IXLWorksheet dataSheet,
            IDictionary<string, ScheduleRowMapEntry> rowMapEntriesByToken,
            int row,
            out ScheduleRowMapEntry? entry,
            out string mappingSource)
        {
            entry = null;
            mappingSource = string.Empty;
            if (dataSheet == null || rowMapEntriesByToken == null)
            {
                mappingSource = "row-token-metadata-unavailable";
                return false;
            }

            var rowToken = GetWorksheetRowToken(dataSheet, row);
            if (string.IsNullOrWhiteSpace(rowToken))
            {
                mappingSource = "row-token-missing";
                return false;
            }

            if (!rowMapEntriesByToken.TryGetValue(rowToken, out entry) || entry == null)
            {
                mappingSource = "row-token-unknown";
                return false;
            }

            mappingSource = "row-token-metadata";
            return true;
        }

        public static bool IsWorkbookCellEdited(IXLWorksheet dataSheet, IXLWorksheet baselineSheet, int row, int baselineRow, int col)
        {
            var current = dataSheet.Cell(row, col).GetString();
            var baseline = baselineSheet.Cell(Math.Max(1, baselineRow), col).GetString();
            return !ExportValuesEqual(current, baseline);
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
                    SourceFieldName = meta.Cell(row, 9).GetString(),
                    ResolvedScope = NormalizeResolvedScope(meta.Cell(row, 10).GetString())
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
            ws.Cell(4, 1).Value = "3. hidden 列(__ElementId, __RowToken, __RowIdentity)は行と一緒に移動させてください。";
            ws.Cell(5, 1).Value = "4. 色の意味: 灰色=変更不可、薄黄色=インスタンス編集可、薄水色=タイプ編集可、薄緑=Yes/No。";
            ws.Cell(6, 1).Value = "5. 編集後は import_schedule_roundtrip_excel で Revit に反映します。";
            ws.Cell(7, 1).Value = "6. hidden の __RowToken を削除・重複させないでください。新方式では __RowToken が行対応の主キーです。";
            ws.Cell(8, 1).Value = "7. 行全体の並べ替えは可能です。列の並べ替え、セル範囲だけの切り取り/貼り付け、行の複製、行の挿入はしないでください。";
            ws.Cell(9, 1).Value = "8. 行削除は Revit の削除ではありません。削除した行は更新されないだけです。";
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
                    if (IsTypeLikeScope(col.ResolvedScope))
                        rng.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEEFF");
                    else
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

        public static string GetPreviewComparableValue(Parameter param, ScheduleRoundtripColumn col)
        {
            try
            {
                if (col.IsBoolean || IsYesNoParameter(param))
                    return param.AsInteger() != 0 ? "1" : "0";

                switch (param.StorageType)
                {
                    case StorageType.String:
                        return (param.AsString() ?? string.Empty).Trim();
                    case StorageType.Integer:
                        return param.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        return (param.AsValueString() ?? string.Empty).Trim();
                    case StorageType.ElementId:
                        var id = param.AsElementId();
                        return id == null || id == ElementId.InvalidElementId
                            ? string.Empty
                            : id.IntValue().ToString(CultureInfo.InvariantCulture);
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetComparableValueFromWorksheetText(Parameter param, ScheduleRoundtripColumn col, string rawText)
        {
            if (TryNormalizeImportedPreviewValue(param, col, rawText, out _, out var comparableValue, out _))
                return comparableValue;

            return (rawText ?? string.Empty).Trim();
        }

        public static bool TryNormalizeImportedPreviewValue(
            Parameter param,
            ScheduleRoundtripColumn col,
            string rawText,
            out string normalizedDisplay,
            out string comparableValue,
            out string message)
        {
            normalizedDisplay = string.Empty;
            comparableValue = string.Empty;
            message = string.Empty;

            var text = (rawText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                message = "blank-skip";
                return false;
            }

            if (col.IsBoolean || IsYesNoParameter(param))
            {
                if (!TryParseBooleanText(text, out var b))
                {
                    message = "Expected ☑/☐.";
                    return false;
                }

                normalizedDisplay = b ? "☑" : "☐";
                comparableValue = b ? "1" : "0";
                return true;
            }

            switch (param.StorageType)
            {
                case StorageType.String:
                    normalizedDisplay = text;
                    comparableValue = text;
                    return true;
                case StorageType.Integer:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                    {
                        normalizedDisplay = iv.ToString(CultureInfo.InvariantCulture);
                        comparableValue = normalizedDisplay;
                        return true;
                    }
                    message = "Expected integer.";
                    return false;
                case StorageType.Double:
                    normalizedDisplay = text;
                    comparableValue = text;
                    return true;
                case StorageType.ElementId:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    {
                        normalizedDisplay = eid.ToString(CultureInfo.InvariantCulture);
                        comparableValue = normalizedDisplay;
                        return true;
                    }
                    message = "Expected ElementId integer.";
                    return false;
                default:
                    message = "Unsupported StorageType.";
                    return false;
            }
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
            var target = FindPreferredElementIdSchedulableField(doc, available);

            if (target == null) return;

            ScheduleField? existing = null;
            var staleHiddenHeaderFields = new List<ScheduleField>();
            foreach (var fid in def.GetFieldOrder())
            {
                var f = def.GetField(fid);
                if (f == null)
                    continue;

                try
                {
                    if (string.Equals(GetFieldHeader(f), HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                        staleHiddenHeaderFields.Add(f);
                }
                catch { }

                try
                {
                    if (target != null && f.ParameterId != null && target.ParameterId != null &&
                        f.ParameterId.IntValue() == target.ParameterId.IntValue())
                    {
                        existing = f;
                    }
                }
                catch { }
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

            foreach (var stale in staleHiddenHeaderFields)
            {
                if (existing != null && ReferenceEquals(stale, existing))
                    continue;

                try { stale.IsHidden = true; } catch { }
                try
                {
                    if (string.Equals(GetFieldHeader(stale), HiddenIdHeader, StringComparison.OrdinalIgnoreCase))
                        stale.ColumnHeading = "__ElementId_old";
                }
                catch { }
            }
        }

        private static List<PropertyInfo> GetPublicInstancePropertiesByName(Type type, string propName)
        {
            try
            {
                return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => string.Equals(p.Name, propName, StringComparison.Ordinal))
                    .ToList();
            }
            catch
            {
                return new List<PropertyInfo>();
            }
        }

        private static void TrySetPropertyIfExists(object target, string propName, object? value)
        {
            if (target == null) return;
            var p = GetPublicInstancePropertiesByName(target.GetType(), propName)
                .FirstOrDefault(x => x.CanWrite && x.GetIndexParameters().Length == 0);
            if (p != null)
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

            var supportAnalysis = ScheduleRoundtripExcelUtil.AnalyzeScheduleRoundtripSupport(doc, schedule);
            if (!supportAnalysis.Supported)
            {
                return new
                {
                    ok = false,
                    code = "SCHEDULE_EXPORT_UNSUPPORTED",
                    msg = supportAnalysis.Reason,
                    supportStatus = supportAnalysis.StatusCode,
                    supportReasonCode = supportAnalysis.ReasonCode,
                    supportReason = supportAnalysis.Reason,
                    suggestedMode = supportAnalysis.SuggestedMode,
                    categoryName = supportAnalysis.CategoryName,
                    visibleColumnCount = supportAnalysis.VisibleColumnCount
                };
            }

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
            ElementId mappingTempId = ElementId.InvalidElementId;

            try
            {
                var work = ScheduleRoundtripExcelUtil.PrepareTemporarySchedule(
                    doc,
                    schedule,
                    out tempId,
                    removeSortGroupFields: exportMode != ScheduleRoundtripExcelUtil.ExportModeDisplay);
                var workTable = work.GetTableData();
                var workBody = workTable.GetSectionData(SectionType.Body);
                int elementIdCol = ScheduleRoundtripExcelUtil.FindElementIdColumnIndex(work);
                var rowElementIds = ScheduleRoundtripExcelUtil.ReadRowElementIds(doc, work);
                bool collectorFallbackUsed = false;
                if (rowElementIds.Count == 0)
                {
                    rowElementIds = ScheduleRoundtripExcelUtil.GetElementsInScheduleView(doc, work);
                    collectorFallbackUsed = rowElementIds.Count > 0;
                }

                if (requestedMode == ScheduleRoundtripExcelUtil.ExportModeAuto
                    && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeRoundtrip, StringComparison.OrdinalIgnoreCase))
                {
                    int visibleBodyRowCount = workBody?.NumberOfRows ?? 0;
                    bool idsLookReliable =
                        elementIdCol >= 0
                        && rowElementIds.Count > 0
                        && (visibleBodyRowCount <= 0 || rowElementIds.Count == visibleBodyRowCount);

                    if (collectorFallbackUsed || !idsLookReliable)
                        exportMode = ScheduleRoundtripExcelUtil.ExportModeDisplay;
                }

                var columnSourceSchedule = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay ? schedule : work;
                var cols = ScheduleRoundtripExcelUtil.BuildColumns(doc, columnSourceSchedule, rowElementIds);
                var displaySchedule = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay ? schedule : work;
                bool sourceIsItemized = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.IsScheduleItemized(schedule)
                    : true;
                var table = displaySchedule.GetTableData();
                var body = table.GetSectionData(SectionType.Body);
                if (body == null)
                    return new { ok = false, msg = "Schedule body section not found." };
                var mappingSourceWork = work;
                var mappingSourceRowElementIds = ScheduleRoundtripExcelUtil.ReadRowElementIds(doc, mappingSourceWork);
                if (mappingSourceRowElementIds.Count == 0)
                    mappingSourceRowElementIds = ScheduleRoundtripExcelUtil.GetElementsInScheduleView(doc, mappingSourceWork);
                if (exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay && sourceIsItemized)
                {
                    mappingSourceWork = ScheduleRoundtripExcelUtil.PrepareTemporarySchedule(
                        doc,
                        schedule,
                        out mappingTempId,
                        removeSortGroupFields: false);
                    mappingSourceRowElementIds = ScheduleRoundtripExcelUtil.ReadRowElementIds(doc, mappingSourceWork);
                    if (mappingSourceRowElementIds.Count == 0)
                        mappingSourceRowElementIds = ScheduleRoundtripExcelUtil.GetElementsInScheduleView(doc, mappingSourceWork);
                }

                var orderedExactDisplayRowElementBuckets = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildOrderedExactDisplayRowElementBuckets(doc, mappingSourceWork, cols, mappingSourceRowElementIds)
                    : new Dictionary<string, Queue<List<int>>>(StringComparer.OrdinalIgnoreCase);
                var orderedExactDisplayRowElementQueues = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay && sourceIsItemized
                    ? ScheduleRoundtripExcelUtil.BuildOrderedExactDisplayRowElementQueues(doc, mappingSourceWork, cols, mappingSourceRowElementIds)
                    : new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
                var displayKeyIndexes = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.GetDisplayKeyColumnIndexes(schedule, cols)
                    : Array.Empty<int>();
                var itemizedHelperRows = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildItemizedDisplayRows(doc, mappingSourceWork, cols, mappingSourceRowElementIds)
                    : Array.Empty<(int ElementId, IList<string> Values)>();
                var liveDisplayElementRows = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? itemizedHelperRows
                    : Array.Empty<(int ElementId, IList<string> Values)>();
                var identityValueElementMap = exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay
                    ? ScheduleRoundtripExcelUtil.BuildIdentityValueElementMap(doc, mappingSourceRowElementIds, cols)
                    : new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                int mappedDisplayRowCount = 0;
                int meaningfulDisplayDataRowCount = 0;
                int expectedMappedDisplayRowCount = 0;
                var exportSessionId = ScheduleRoundtripExcelUtil.BuildExportSessionId();

                using (var wb = new XLWorkbook(XLEventTracking.Disabled))
                {
                    var ws = wb.AddWorksheet(ScheduleRoundtripExcelUtil.DataSheetName);
                    var readme = wb.AddWorksheet(ScheduleRoundtripExcelUtil.ReadmeSheetName);
                    var meta = wb.AddWorksheet(ScheduleRoundtripExcelUtil.MetaSheetName);
                    var rowMapSheet = wb.AddWorksheet(ScheduleRoundtripExcelUtil.RowMapSheetName);
                    var baselineSheet = wb.AddWorksheet(ScheduleRoundtripExcelUtil.BaselineSheetName);
                    var rowMap = new Dictionary<int, List<int>>();
                    var rowMetadata = new Dictionary<int, ScheduleRowMapEntry>();

                    ScheduleRoundtripExcelUtil.WriteReadmeSheet(readme);
                    ScheduleRoundtripExcelUtil.WriteMetaSheet(meta, doc, schedule, cols, exportSessionId);
                    meta.Cell(4, 2).Value = exportMode;

                    ws.Cell(1, 1).Value = ScheduleRoundtripExcelUtil.HiddenIdHeader;
                    ws.Column(1).Hide();
                    int rowTokenColumnNumber = cols.Count + 2;
                    ws.Cell(1, rowTokenColumnNumber).Value = ScheduleRoundtripExcelUtil.HiddenRowTokenHeader;
                    ws.Column(rowTokenColumnNumber).Hide();
                    int rowIdentityColumnNumber = cols.Count + 3;
                    ws.Cell(1, rowIdentityColumnNumber).Value = ScheduleRoundtripExcelUtil.HiddenRowIdentityHeader;
                    ws.Column(rowIdentityColumnNumber).Hide();

                    foreach (var col in cols)
                        ws.Cell(1, col.OutputColumnNumber).Value = col.Header;

                    int outRow = 2;
                    if (exportMode == ScheduleRoundtripExcelUtil.ExportModeDisplay)
                    {
                        var mappingTable = mappingSourceWork.GetTableData();
                        var mappingBody = mappingTable.GetSectionData(SectionType.Body);
                        int mappingElementIdCol = ScheduleRoundtripExcelUtil.FindElementIdColumnIndex(mappingSourceWork);
                        bool emittedMeaningfulDisplayRow = false;
                        var assignedItemizedElementIds = new HashSet<int>();
                        int helperCursor = 0;
                        for (int r = 0; r < body.NumberOfRows; r++)
                        {
                            var rowValues = ScheduleRoundtripExcelUtil.ReadDisplayRowValues(schedule, cols, r);
                            if (rowValues.Count == 0)
                                continue;

                            int meaningfulValueCount = ScheduleRoundtripExcelUtil.CountMeaningfulDisplayValues(rowValues, cols);
                            if (!emittedMeaningfulDisplayRow
                                && ScheduleRoundtripExcelUtil.IsDisplayHeaderLikeRow(rowValues, cols))
                            {
                                continue;
                            }

                            List<int>? mappedIds = null;
                            bool usedSequentialHelper = false;
                            bool mappingRowExplicitlyHasNoElement =
                                ScheduleRoundtripExcelUtil.IsLikelyDisplayNonElementRow(rowValues, cols, displayKeyIndexes);
                            if (meaningfulValueCount > 0 && !mappingRowExplicitlyHasNoElement)
                                meaningfulDisplayDataRowCount++;
                            if (sourceIsItemized
                                && mappingBody != null
                                && mappingElementIdCol >= 0
                                && r >= 0
                                && r < mappingBody.NumberOfRows)
                            {
                                var directIdText = ScheduleRoundtripExcelUtil.GetCellText(
                                    mappingSourceWork,
                                    mappingBody,
                                    SectionType.Body,
                                    r,
                                    mappingElementIdCol);
                                if (int.TryParse((directIdText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var directId))
                                    mappedIds = new List<int> { directId };
                            }

                            if (sourceIsItemized
                                && !mappingRowExplicitlyHasNoElement
                                && (mappedIds == null || mappedIds.Count == 0)
                                && itemizedHelperRows.Count > 0)
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .ConsumeSequentialDisplayRowElementIds(
                                        rowValues,
                                        cols,
                                        itemizedHelperRows,
                                        ref helperCursor,
                                        sourceIsItemized,
                                        displayKeyIndexes)
                                    .ToList();
                                usedSequentialHelper = mappedIds.Count > 0;
                            }

                            if (sourceIsItemized
                                && !mappingRowExplicitlyHasNoElement
                                && (mappedIds == null || mappedIds.Count == 0))
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .ConsumeOrderedExactDisplayRowElementQueue(orderedExactDisplayRowElementQueues, rowValues)
                                    .ToList();
                            }

                            if (sourceIsItemized
                                && !mappingRowExplicitlyHasNoElement
                                && (mappedIds == null || mappedIds.Count == 0)
                                && identityValueElementMap.Count > 0
                                && liveDisplayElementRows.Count > 0)
                            {
                                var singleMatch = ScheduleRoundtripExcelUtil
                                    .MatchDisplayRowToLiveElementIds(rowValues, cols, identityValueElementMap, liveDisplayElementRows)
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();
                                if (singleMatch.Count == 1)
                                    mappedIds = singleMatch;
                            }

                            if (!sourceIsItemized
                                && !mappingRowExplicitlyHasNoElement
                                && (mappedIds == null || mappedIds.Count == 0)
                                && itemizedHelperRows.Count > 0)
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .ConsumeSequentialDisplayRowElementIds(
                                        rowValues,
                                        cols,
                                        itemizedHelperRows,
                                        ref helperCursor,
                                        sourceIsItemized,
                                        displayKeyIndexes)
                                    .ToList();
                                usedSequentialHelper = mappedIds.Count > 0;
                            }

                            if (!sourceIsItemized
                                && (mappedIds == null || mappedIds.Count == 0))
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .ConsumeOrderedExactDisplayRowElementBucket(orderedExactDisplayRowElementBuckets, rowValues)
                                    .ToList();
                            }

                            if (!sourceIsItemized
                                && (mappedIds == null || mappedIds.Count == 0)
                                && identityValueElementMap.Count > 0
                                && liveDisplayElementRows.Count > 0)
                            {
                                mappedIds = ScheduleRoundtripExcelUtil
                                    .MatchDisplayRowToLiveElementIds(rowValues, cols, identityValueElementMap, liveDisplayElementRows)
                                    .ToList();
                            }

                            if (sourceIsItemized && mappedIds != null && mappedIds.Count == 1)
                            {
                                if (!ScheduleRoundtripExcelUtil.DoesElementMatchDisplayIdentityValues(doc, mappedIds[0], rowValues, cols))
                                {
                                    mappedIds = ScheduleRoundtripExcelUtil
                                        .MatchDisplayRowToLiveElementIds(rowValues, cols, identityValueElementMap, liveDisplayElementRows)
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();
                                }
                            }

                            if (!usedSequentialHelper
                                && !mappingRowExplicitlyHasNoElement
                                && mappedIds != null
                                && mappedIds.Count > 0
                                && itemizedHelperRows.Count > 0)
                            {
                                ScheduleRoundtripExcelUtil.TryAdvanceHelperCursor(
                                    itemizedHelperRows,
                                    ref helperCursor,
                                    mappedIds);
                            }

                            if (sourceIsItemized && mappedIds != null && mappedIds.Count > 0)
                            {
                                mappedIds = mappedIds
                                    .Where(id => id > 0 && !assignedItemizedElementIds.Contains(id))
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();
                            }

                            if (sourceIsItemized && mappedIds != null && mappedIds.Count > 1)
                                mappedIds = new List<int>();

                            if (sourceIsItemized && mappedIds != null && mappedIds.Count == 1)
                                assignedItemizedElementIds.Add(mappedIds[0]);

                            ws.Cell(outRow, 1).Value = string.Join(";", mappedIds ?? new List<int>());
                            rowMap[outRow] = (mappedIds ?? new List<int>()).Distinct().OrderBy(x => x).ToList();
                            var rowToken = ScheduleRoundtripExcelUtil.BuildRowToken(exportSessionId, outRow);
                            ws.Cell(outRow, rowTokenColumnNumber).Value = rowToken;
                            if (rowMap[outRow].Count > 0)
                                mappedDisplayRowCount++;
                            var rowIdentity = ScheduleRoundtripExcelUtil.BuildWorksheetRowIdentity(outRow, rowMap[outRow]);
                            ws.Cell(outRow, rowIdentityColumnNumber).Value = rowIdentity;

                            for (int colIndex = 0; colIndex < cols.Count && colIndex < rowValues.Count; colIndex++)
                                ws.Cell(outRow, cols[colIndex].OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(
                                        null!,
                                        cols[colIndex],
                                        rowValues[colIndex]);

                            if (meaningfulValueCount > 0)
                                emittedMeaningfulDisplayRow = true;

                            rowMetadata[outRow] = ScheduleRoundtripExcelUtil.BuildRowMapEntry(
                                doc,
                                outRow,
                                rowToken,
                                rowIdentity,
                                rowMap[outRow],
                                rowValues,
                                cols,
                                meaningfulValueCount > 0 && !mappingRowExplicitlyHasNoElement);

                            outRow++;
                        }

                        if (!sourceIsItemized
                            && itemizedHelperRows.Count > 0
                            && helperCursor >= itemizedHelperRows.Count)
                        {
                            foreach (var entry in rowMetadata.Values)
                            {
                                if (entry == null)
                                    continue;
                                if (entry.ElementIds != null && entry.ElementIds.Count > 0)
                                    continue;
                                entry.MeaningfulRow = false;
                            }

                            meaningfulDisplayDataRowCount = rowMetadata.Values.Count(x => x != null && x.MeaningfulRow);
                        }

                        expectedMappedDisplayRowCount = meaningfulDisplayDataRowCount;

                        if (expectedMappedDisplayRowCount > 0 && mappedDisplayRowCount == 0)
                        {
                            return new
                            {
                                ok = false,
                                code = "SCHEDULE_EXPORT_UNSUPPORTED",
                                msg = "対応不可: 集計表の表示行を Revit 要素 ID に対応付けできませんでした。",
                                supportStatus = "unsupported",
                                supportReasonCode = "DISPLAY_ROW_MAPPING_FAILED",
                                supportReason = "対応不可: 集計表の表示行を Revit 要素 ID に対応付けできませんでした。",
                                suggestedMode = exportMode
                            };
                        }
                        if (expectedMappedDisplayRowCount > 0 && mappedDisplayRowCount < expectedMappedDisplayRowCount)
                        {
                            return new
                            {
                                ok = false,
                                code = "SCHEDULE_EXPORT_UNSUPPORTED",
                                msg = $"対応不可: 集計表の表示行 {mappedDisplayRowCount}/{expectedMappedDisplayRowCount} 行しか要素 ID に対応付けできません。",
                                supportStatus = "unsupported",
                                supportReasonCode = "DISPLAY_PARTIAL_ROW_MAPPING",
                                supportReason = $"対応不可: 集計表の表示行 {mappedDisplayRowCount}/{expectedMappedDisplayRowCount} 行しか要素 ID に対応付けできません。",
                                suggestedMode = exportMode,
                                mappedRowCount = mappedDisplayRowCount,
                                meaningfulRowCount = expectedMappedDisplayRowCount
                            };
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
                            var rowToken = ScheduleRoundtripExcelUtil.BuildRowToken(exportSessionId, outRow);
                            ws.Cell(outRow, rowTokenColumnNumber).Value = rowToken;
                            var rowIdentity = ScheduleRoundtripExcelUtil.BuildWorksheetRowIdentity(outRow, rowMap[outRow]);
                            ws.Cell(outRow, rowIdentityColumnNumber).Value = rowIdentity;
                            foreach (var col in cols)
                            {
                                ws.Cell(outRow, col.OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(element, col, string.Empty);
                            }
                            rowMetadata[outRow] = ScheduleRoundtripExcelUtil.BuildRowMapEntry(
                                doc,
                                outRow,
                                rowToken,
                                rowIdentity,
                                rowMap[outRow],
                                ScheduleRoundtripExcelUtil.ReadWorksheetRowValuesByColumns(ws, outRow, cols),
                                cols,
                                meaningfulRow: true);
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
                            var rowToken = ScheduleRoundtripExcelUtil.BuildRowToken(exportSessionId, outRow);
                            ws.Cell(outRow, rowTokenColumnNumber).Value = rowToken;
                            var rowIdentity = ScheduleRoundtripExcelUtil.BuildWorksheetRowIdentity(outRow, rowMap[outRow]);
                            ws.Cell(outRow, rowIdentityColumnNumber).Value = rowIdentity;
                            foreach (var col in cols)
                            {
                                var displayText = ScheduleRoundtripExcelUtil.GetCellText(work, body, SectionType.Body, r, col.ScheduleColumnIndex);
                                ws.Cell(outRow, col.OutputColumnNumber).Value =
                                    ScheduleRoundtripExcelUtil.GetExportValueForColumn(element, col, displayText);
                            }
                            rowMetadata[outRow] = ScheduleRoundtripExcelUtil.BuildRowMapEntry(
                                doc,
                                outRow,
                                rowToken,
                                rowIdentity,
                                rowMap[outRow],
                                ScheduleRoundtripExcelUtil.ReadWorksheetRowValuesByColumns(ws, outRow, cols),
                                cols,
                                meaningfulRow: true);
                            outRow++;
                        }
                    }

                    int lastRow = Math.Max(2, outRow - 1);
                    int lastCol = Math.Max(rowIdentityColumnNumber, cols.Count + 1);

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
                    ScheduleRoundtripExcelUtil.WriteRowMapSheet(rowMapSheet, rowMap, rowMetadata, exportMode);
                    ScheduleRoundtripExcelUtil.WriteBaselineSheet(baselineSheet, ws, lastRow, lastCol);

                    wb.Worksheet(ScheduleRoundtripExcelUtil.DataSheetName).SetTabActive();
                    wb.SaveAs(filePath);
                }

                return new
                {
                    ok = true,
                    path = filePath,
                    requestedMode,
                    mode = exportMode,
                    schemaVersion = ScheduleRoundtripExcelUtil.SchemaVersionV2,
                    rowResolutionMode = ScheduleRoundtripExcelUtil.RowResolutionModeAuthoritative,
                    scheduleViewId = schedule.Id.IntValue(),
                    scheduleName = schedule.Name,
                    itemizedRowCount = rowElementIds.Count,
                    exportedRowCount = Math.Max(0, (body?.NumberOfRows ?? 0)),
                    mappedRowCount = mappedDisplayRowCount,
                    unmappedMeaningfulRowCount = Math.Max(0, meaningfulDisplayDataRowCount - mappedDisplayRowCount),
                    editableColumnCount = cols.Count(c => c.Editable),
                    booleanColumnCount = cols.Count(c => c.IsBoolean),
                    collectorFallbackUsed,
                    supportStatus = supportAnalysis.StatusCode,
                    supportReasonCode = supportAnalysis.ReasonCode,
                    supportReason = supportAnalysis.Reason
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Roundtrip Excel export failed.", detail = ex.Message };
            }
            finally
            {
                ScheduleRoundtripExcelUtil.CleanupTempSchedule(doc, tempId);
                ScheduleRoundtripExcelUtil.CleanupTempSchedule(doc, mappingTempId);
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
            var ext = Path.GetExtension(filePath ?? string.Empty);
            if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".xltx", StringComparison.OrdinalIgnoreCase))
            {
                return new { ok = false, msg = "Only .xlsx and .xltx are supported. Macro-enabled Excel files are not accepted." };
            }

            var reportPath = p.Value<string>("reportPath") ?? ScheduleRoundtripExcelUtil.GetDefaultImportReportPath(filePath);
            var auditJsonPath = p.Value<string>("auditJsonPath") ?? Path.ChangeExtension(reportPath, ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? Path.GetTempPath());
            Directory.CreateDirectory(Path.GetDirectoryName(auditJsonPath) ?? Path.GetTempPath());
            var expectedValueMap = ScheduleRoundtripExcelUtil.ReadExpectedValues(p["expectedValues"]);
            var expectedValueCellMap = ScheduleRoundtripExcelUtil.GroupExpectedValuesByCell(expectedValueMap.Values);

            try
            {
                using (var wb = new XLWorkbook(filePath, XLEventTracking.Disabled))
                using (var fhScope = new FailureHandlingScope(uiapp, FailureHandlingMode.Off))
                {
                    var dataSheetName = p.Value<string>("sheetName") ?? ScheduleRoundtripExcelUtil.DataSheetName;
                    var ws = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, dataSheetName, StringComparison.OrdinalIgnoreCase))
                             ?? wb.Worksheets.FirstOrDefault(x => !string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.RowMapSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.BaselineSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.ReadmeSheetName, StringComparison.OrdinalIgnoreCase));
                    if (ws == null)
                        return new { ok = false, msg = "Data sheet not found in workbook." };

                    var meta = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase));
                    if (meta == null)
                        return new { ok = false, msg = "Metadata sheet not found. Use export_schedule_roundtrip_excel output." };
                    var allCols = ScheduleRoundtripExcelUtil.ReadMetaSheet(meta).ToList();
                    var cols = allCols.Where(c => c.Editable).ToList();
                    if (cols.Count == 0)
                        return new { ok = false, msg = "No editable columns found in metadata." };
                    var baselineSheet = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.BaselineSheetName, StringComparison.OrdinalIgnoreCase));
                    if (baselineSheet == null)
                        return new { ok = false, msg = "Baseline snapshot sheet not found. Re-export the schedule with the current build before importing." };
                    if (!ScheduleRoundtripExcelUtil.ValidateWorkbookDocumentScope(doc, meta, out var workbookScopeMessage))
                        return new { ok = false, msg = workbookScopeMessage };
                    var schemaVersion = ScheduleRoundtripExcelUtil.GetWorkbookSchemaVersion(meta);
                    bool rowTokenAuthoritative = ScheduleRoundtripExcelUtil.IsRowTokenAuthoritativeWorkbook(meta);
                    if (!ScheduleRoundtripExcelUtil.ValidateWorkbookLayout(meta, ws, baselineSheet, allCols, out var workbookLayoutMessage))
                        return new { ok = false, msg = workbookLayoutMessage };
                    var baselineRows = ScheduleRoundtripExcelUtil.BuildBaselineRows(baselineSheet, allCols);
                    var baselineRowTokenLookup = ScheduleRoundtripExcelUtil.BuildBaselineRowTokenLookup(baselineRows);
                    var baselineRowIdentityLookup = ScheduleRoundtripExcelUtil.BuildBaselineRowIdentityLookup(baselineRows);
                    var baselineHiddenIdLookup = ScheduleRoundtripExcelUtil.BuildBaselineHiddenIdLookup(baselineRows);
                    var baselineVisibleRowLookup = ScheduleRoundtripExcelUtil.BuildBaselineVisibleRowLookup(baselineRows);
                    var rowMapSheet = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.RowMapSheetName, StringComparison.OrdinalIgnoreCase));
                    var rowMap = ScheduleRoundtripExcelUtil.ReadRowMapSheet(rowMapSheet);
                    var rowMapEntriesByToken = rowTokenAuthoritative
                        ? ScheduleRoundtripExcelUtil.ReadRowMapEntriesByToken(rowMapSheet)
                        : new Dictionary<string, ScheduleRowMapEntry>(StringComparer.OrdinalIgnoreCase);

                    int updatedCount = 0;
                    int changedCount = 0;
                    int unchangedCount = 0;
                    int skippedCount = 0;
                    int failedCount = 0;
                    int conflictCount = 0;
                    var workbookTargetRequests = new Dictionary<string, ScheduleTargetRequest>(StringComparer.OrdinalIgnoreCase);
                    var reportRows = new List<string> { "row,elementId,updated,skipped,failed,message" };
                    var auditChanges = new List<object>();
                    var auditRows = new List<ScheduleImportAuditRowResult>();
                    var exportMode = meta.Cell(4, 2).GetString();
                    bool allowHeuristicDisplayWriteRemap =
                        !rowTokenAuthoritative
                        && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase);
                    int missingElementIdRows = 0;
                    ViewSchedule? displaySchedule = null;
                    ElementId tempId = ElementId.InvalidElementId;
                    IList<ScheduleRoundtripColumn> displayCols = Array.Empty<ScheduleRoundtripColumn>();
                    IList<(int ElementId, IList<string> Values)> itemizedDisplayRows = Array.Empty<(int ElementId, IList<string> Values)>();
                    IDictionary<string, int> worksheetHeaderColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    IDictionary<string, int> baselineHeaderColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    IDictionary<int, List<int>> baselineSequentialRowElementMap = new Dictionary<int, List<int>>();
                    IDictionary<string, List<int>> identityValueElementMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    IList<int> displayKeyIndexes = Array.Empty<int>();

                    try
                    {
                        if (allowHeuristicDisplayWriteRemap
                            && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase))
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
                                displayKeyIndexes = ScheduleRoundtripExcelUtil.GetDisplayKeyColumnIndexes(displaySchedule, displayCols);
                                identityValueElementMap = ScheduleRoundtripExcelUtil.BuildIdentityValueElementMap(doc, mappingRowElementIds, displayCols);
                                itemizedDisplayRows = ScheduleRoundtripExcelUtil.BuildItemizedDisplayRows(doc, mappingWork, displayCols, mappingRowElementIds);
                                worksheetHeaderColumnMap = ScheduleRoundtripExcelUtil.BuildWorksheetHeaderColumnMap(ws);
                                baselineHeaderColumnMap = ScheduleRoundtripExcelUtil.BuildWorksheetHeaderColumnMap(baselineSheet);
                                baselineSequentialRowElementMap = ScheduleRoundtripExcelUtil.BuildSequentialBaselineRowElementMap(
                                    baselineSheet,
                                    displayCols,
                                    baselineHeaderColumnMap,
                                    itemizedDisplayRows,
                                    ScheduleRoundtripExcelUtil.IsScheduleItemized(displaySchedule),
                                    displayKeyIndexes);
                            }
                        }
                    }
                    catch { }

                    using (var tx = new Transaction(doc, "Import Schedule Roundtrip Excel"))
                    {
                        tx.Start();
                        TxnUtil.ConfigureProceedWithWarnings(tx);

                        var usedDisplayFallbackElementIds = new HashSet<int>();
                        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                        for (int row = 2; row <= lastRow; row++)
                        {
                            var baselineResolution = ScheduleRoundtripExcelUtil.ResolveBaselineRow(
                                ws,
                                allCols,
                                baselineRows,
                                baselineRowTokenLookup,
                                baselineRowIdentityLookup,
                                baselineHiddenIdLookup,
                                baselineVisibleRowLookup,
                                row,
                                allowVisibleFallback: false);
                            var baselineRow = baselineResolution.RowNumber;
                            var editedCols = cols
                                .Where(col =>
                                {
                                    var text = ws.Cell(row, col.OutputColumnNumber).GetString();
                                    return !string.IsNullOrWhiteSpace(text)
                                           && ScheduleRoundtripExcelUtil.IsWorkbookCellEdited(ws, baselineSheet, row, baselineRow, col.OutputColumnNumber);
                                })
                                .ToList();
                            if (editedCols.Count == 0)
                                continue;

                            List<int> rowElementIds;
                            string mappingSource;
                            bool stableBaselineResolution =
                                !string.Equals(baselineResolution.Source, "row-number", StringComparison.OrdinalIgnoreCase);
                            if (rowTokenAuthoritative)
                            {
                                if (ScheduleRoundtripExcelUtil.TryGetWorksheetRowMapEntryByToken(
                                    ws,
                                    rowMapEntriesByToken,
                                    row,
                                    out var rowEntry,
                                    out mappingSource)
                                    && rowEntry != null)
                                {
                                    rowElementIds = (rowEntry.ElementIds ?? new List<int>())
                                        .Where(id => id > 0)
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();
                                }
                                else
                                {
                                    rowElementIds = new List<int>();
                                }
                            }
                            else
                            {
                                rowElementIds = ScheduleRoundtripExcelUtil.ResolveWorksheetRowElementIds(
                                    ws,
                                    baselineSheet,
                                    rowMap,
                                    row,
                                    baselineResolution).ToList();
                                mappingSource = baselineResolution.Source;
                                if (string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(mappingSource, "row-number", StringComparison.OrdinalIgnoreCase))
                                {
                                    rowElementIds.Clear();
                                    mappingSource = "row-identity-missing";
                                }
                                if (string.Equals(mappingSource, "row-number", StringComparison.OrdinalIgnoreCase))
                                    mappingSource = rowElementIds.Count > 0 ? "worksheet-hidden-id-column" : string.Empty;
                                if (rowElementIds.Count == 0
                                    && stableBaselineResolution
                                    && allowHeuristicDisplayWriteRemap
                                    && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                    && baselineSequentialRowElementMap.TryGetValue(baselineRow, out var sequentialIds)
                                    && sequentialIds != null
                                    && sequentialIds.Count > 0)
                                {
                                    rowElementIds = sequentialIds
                                        .Where(id => id > 0)
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();
                                    if (rowElementIds.Count > 0)
                                        mappingSource = "baseline-helper-sequence";
                                }
                                if (rowElementIds.Count == 0
                                    && allowHeuristicDisplayWriteRemap
                                    && stableBaselineResolution
                                    && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                    && displayCols.Count > 0
                                    && itemizedDisplayRows.Count > 0)
                                {
                                    var displayRowValues = ScheduleRoundtripExcelUtil.ReadWorksheetDisplayRowValues(
                                        baselineSheet,
                                        baselineRow,
                                        displayCols,
                                        baselineHeaderColumnMap);
                                    if (ScheduleRoundtripExcelUtil.IsLikelyDisplayNonElementRow(displayRowValues, displayCols, displayKeyIndexes))
                                        displayRowValues = Array.Empty<string>();
                                    rowElementIds = ScheduleRoundtripExcelUtil
                                        .MatchDisplayRowToLiveElementIds(displayRowValues, displayCols, identityValueElementMap, itemizedDisplayRows)
                                        .ToList();
                                    rowElementIds = rowElementIds
                                        .Where(id => id > 0 && !usedDisplayFallbackElementIds.Contains(id))
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();
                                    if (rowElementIds.Count > 0)
                                        mappingSource = "baseline-display-live-match";
                                }
                            }
                            foreach (var id in rowElementIds)
                                usedDisplayFallbackElementIds.Add(id);
                            var rowAudit = new ScheduleImportAuditRowResult
                            {
                                Row = row,
                                MappingSource = string.IsNullOrWhiteSpace(mappingSource) ? "unmapped" : mappingSource,
                                ElementIds = rowElementIds.ToList()
                            };
                            if (rowElementIds.Count == 0)
                            {
                                skippedCount++;
                                missingElementIdRows++;
                                rowAudit.Skipped = 1;
                                rowAudit.Message = "missing-element-id";
                                reportRows.Add($"{row},,0,1,0,missing-element-id");
                                auditRows.Add(rowAudit);
                                continue;
                            }

                            int rowUpdated = 0;
                            int rowSkipped = 0;
                            int rowFailed = 0;
                            var rowMessages = new List<string>();

                            foreach (var col in editedCols)
                            {
                                var cellText = ws.Cell(row, col.OutputColumnNumber).GetString();
                                var cellAudit = new ScheduleImportAuditCellResult
                                {
                                    OutputColumnNumber = col.OutputColumnNumber,
                                    Header = col.Header,
                                    ParameterName = col.ParamName,
                                    ImportedValue = cellText ?? string.Empty
                                };
                                if (string.IsNullOrWhiteSpace(cellText))
                                {
                                    rowSkipped++;
                                    cellAudit.Status = "skipped";
                                    cellAudit.Message = "blank-cell";
                                    rowAudit.Cells.Add(cellAudit);
                                    continue;
                                }

                                var expectedCellKey = ScheduleRoundtripExcelUtil.BuildExpectedCellKey(row, col.OutputColumnNumber);
                                var hasExpectedTargets = expectedValueCellMap.TryGetValue(expectedCellKey, out var expectedTargetsForCell)
                                    && expectedTargetsForCell != null
                                    && expectedTargetsForCell.Count > 0;
                                if (expectedValueMap.Count > 0 && !hasExpectedTargets)
                                {
                                    rowFailed++;
                                    conflictCount++;
                                    var conflictMessage = "preview-guard-missing";
                                    rowMessages.Add($"{col.Header}:{conflictMessage}");
                                    cellAudit.Status = "conflict";
                                    cellAudit.Message = conflictMessage;
                                    rowAudit.Cells.Add(cellAudit);
                                    continue;
                                }

                                var targetElementIdsForCell = hasExpectedTargets
                                    ? expectedTargetsForCell!
                                        .Select(x => x.ElementId)
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList()
                                    : rowElementIds
                                        .Distinct()
                                        .OrderBy(x => x)
                                        .ToList();
                                var processedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var eid in targetElementIdsForCell)
                                {
                                    var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                                    if (element == null)
                                    {
                                        rowFailed++;
                                        rowMessages.Add($"{col.Header}:element-not-found({eid})");
                                        cellAudit.Status = "failed";
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = eid,
                                            Status = "failed",
                                            Imported = cellText ?? string.Empty,
                                            Message = "element-not-found"
                                        });
                                        continue;
                                    }

                                    Element? owner;
                                    string scope;
                                    var prm = hasExpectedTargets
                                        ? ScheduleRoundtripExcelUtil.ResolveParameterOnExplicitTarget(element, col, out owner, out scope)
                                        : ScheduleRoundtripExcelUtil.ResolveParameterByFixedScope(element, col, out owner, out scope);
                                    if (prm == null || prm.IsReadOnly)
                                    {
                                        rowSkipped++;
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = eid,
                                            Status = "skipped",
                                            Imported = cellText ?? string.Empty,
                                            Message = prm == null ? "parameter-not-found" : "parameter-readonly"
                                        });
                                        continue;
                                    }

                                    var resolvedTargetKey = ScheduleRoundtripExcelUtil.BuildResolvedTargetKey(owner, scope, eid);
                                    if (!processedTargets.Add(resolvedTargetKey))
                                        continue;

                                    var targetElementId = hasExpectedTargets
                                        ? eid
                                        : ScheduleRoundtripExcelUtil.GetResolvedTargetElementId(owner, eid);
                                    var valueOwner = owner ?? element;

                                    if (prm.StorageType == StorageType.ElementId
                                        && !int.TryParse((cellText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                                    {
                                        rowSkipped++;
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = "skipped",
                                            Imported = cellText ?? string.Empty,
                                            Message = "elementid-text-not-supported"
                                        });
                                        continue;
                                    }

                                    var beforeValue = ScheduleRoundtripExcelUtil.GetExportValueForColumn(valueOwner, col, prm.AsValueString() ?? prm.AsString() ?? string.Empty);
                                    var beforeComparable = ScheduleRoundtripExcelUtil.GetPreviewComparableValue(prm, col);
                                    var baselineValue = baselineSheet.Cell(baselineRow, col.OutputColumnNumber).GetString();
                                    var baselineComparable = ScheduleRoundtripExcelUtil.GetComparableValueFromWorksheetText(prm, col, baselineValue);
                                    var importedComparable = ScheduleRoundtripExcelUtil.GetComparableValueFromWorksheetText(prm, col, cellText ?? string.Empty);
                                    var targetRequestKey = ScheduleRoundtripExcelUtil.BuildTargetRequestKey(col.OutputColumnNumber, targetElementId);
                                    if (workbookTargetRequests.TryGetValue(targetRequestKey, out var existingTargetRequest))
                                    {
                                        if (!ScheduleRoundtripExcelUtil.ExportValuesEqual(existingTargetRequest.ImportedComparable, importedComparable)
                                            && !ScheduleRoundtripExcelUtil.ExportValuesEqual(existingTargetRequest.ImportedDisplay, cellText))
                                        {
                                            rowFailed++;
                                            conflictCount++;
                                            var conflictMessage = $"same-target-edited-with-different-values(row-{existingTargetRequest.Row})";
                                            rowMessages.Add($"{col.Header}[{targetElementId}]:{conflictMessage}");
                                            cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                            {
                                                ElementId = targetElementId,
                                                Status = "conflict",
                                                Before = beforeValue,
                                                Imported = cellText ?? string.Empty,
                                                After = beforeValue,
                                                Message = conflictMessage
                                            });
                                        }
                                        else
                                        {
                                            cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                            {
                                                ElementId = targetElementId,
                                                Status = "skipped",
                                                Before = beforeValue,
                                                Imported = cellText ?? string.Empty,
                                                After = beforeValue,
                                                Message = $"same-target-already-applied(row-{existingTargetRequest.Row})"
                                            });
                                        }
                                        continue;
                                    }

                                    workbookTargetRequests[targetRequestKey] = new ScheduleTargetRequest
                                    {
                                        Row = row,
                                        OutputColumnNumber = col.OutputColumnNumber,
                                        TargetElementId = targetElementId,
                                        Scope = scope ?? string.Empty,
                                        ImportedComparable = importedComparable,
                                        ImportedDisplay = cellText ?? string.Empty
                                    };
                                    var importedMatchesCurrent = ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeComparable, importedComparable)
                                        || ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeValue, cellText);
                                    var liveChangedSinceExport = !ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeComparable, baselineComparable)
                                        && !ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeValue, baselineValue);
                                    var expectedKey = ScheduleRoundtripExcelUtil.BuildExpectedValueKey(row, col.OutputColumnNumber, targetElementId);
                                    if (!importedMatchesCurrent && liveChangedSinceExport)
                                    {
                                        rowFailed++;
                                        conflictCount++;
                                        var conflictMessage = "current-value-changed-since-export";
                                        rowMessages.Add($"{col.Header}[{targetElementId}]:{conflictMessage}");
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = "conflict",
                                            Before = beforeValue,
                                            Imported = cellText ?? string.Empty,
                                            After = beforeValue,
                                            Message = conflictMessage
                                        });
                                        continue;
                                    }

                                    if (expectedValueMap.Count > 0 && !expectedValueMap.TryGetValue(expectedKey, out var expected))
                                    {
                                        rowFailed++;
                                        conflictCount++;
                                        var conflictMessage = "preview-guard-missing";
                                        rowMessages.Add($"{col.Header}[{targetElementId}]:{conflictMessage}");
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = "conflict",
                                            Before = beforeValue,
                                            Imported = cellText ?? string.Empty,
                                            After = beforeValue,
                                            Message = conflictMessage
                                        });
                                        continue;
                                    }

                                    if (expectedValueMap.TryGetValue(expectedKey, out expected))
                                    {
                                        if (!expected.CanApply)
                                        {
                                            rowFailed++;
                                            conflictCount++;
                                            var conflictMessage = "preview-conflict-live-value-changed-since-export";
                                            rowMessages.Add($"{col.Header}[{targetElementId}]:{conflictMessage}");
                                            cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                            {
                                                ElementId = targetElementId,
                                                Status = "conflict",
                                                Before = beforeValue,
                                                Imported = cellText ?? string.Empty,
                                                After = beforeValue,
                                                Message = conflictMessage
                                            });
                                            continue;
                                        }

                                        if (!string.Equals(beforeComparable, expected.ExpectedComparable, StringComparison.Ordinal))
                                        {
                                            rowFailed++;
                                            conflictCount++;
                                            var conflictMessage = "current-value-changed-since-preview";
                                            rowMessages.Add($"{col.Header}[{targetElementId}]:{conflictMessage}");
                                            cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                            {
                                                ElementId = targetElementId,
                                                Status = "conflict",
                                                Before = beforeValue,
                                                Imported = cellText ?? string.Empty,
                                                After = beforeValue,
                                                Message = conflictMessage
                                            });
                                            continue;
                                        }
                                    }

                                    if (ScheduleRoundtripExcelUtil.TryApplyImportedValue(prm, col, cellText, out var msg))
                                    {
                                        rowUpdated++;
                                        var afterValue = ScheduleRoundtripExcelUtil.GetExportValueForColumn(valueOwner, col, prm.AsValueString() ?? prm.AsString() ?? string.Empty);
                                        var changed = !ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeValue, afterValue);
                                        if (changed)
                                            changedCount++;
                                        else
                                            unchangedCount++;

                                        var changeRecord = new
                                        {
                                            row,
                                            elementId = targetElementId,
                                            parameter = col.Header,
                                            parameterName = col.ParamName,
                                            before = beforeValue,
                                            imported = cellText,
                                            after = afterValue,
                                            changed,
                                            mode = exportMode,
                                            mappingSource = rowAudit.MappingSource,
                                            appliedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture)
                                        };
                                        if (changed)
                                            auditChanges.Add(changeRecord);

                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = changed ? "changed" : "unchanged",
                                            Before = beforeValue,
                                            Imported = cellText ?? string.Empty,
                                            After = afterValue,
                                            Message = changed ? "value-changed" : "no-effective-change"
                                        });
                                    }
                                    else if (string.Equals(msg, "blank-skip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        rowSkipped++;
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = "skipped",
                                            Before = beforeValue,
                                            Imported = cellText ?? string.Empty,
                                            After = beforeValue,
                                            Message = "blank-skip"
                                        });
                                    }
                                    else
                                    {
                                        rowFailed++;
                                        rowMessages.Add($"{col.Header}[{targetElementId}]:{msg}");
                                        cellAudit.Elements.Add(new ScheduleImportAuditElementResult
                                        {
                                            ElementId = targetElementId,
                                            Status = "failed",
                                            Before = beforeValue,
                                            Imported = cellText ?? string.Empty,
                                            After = beforeValue,
                                            Message = msg ?? string.Empty
                                        });
                                    }
                                }

                                if (cellAudit.Elements.Count == 0)
                                {
                                    cellAudit.Status = "skipped";
                                    if (string.IsNullOrWhiteSpace(cellAudit.Message))
                                        cellAudit.Message = "no-target-elements";
                                }
                                else if (cellAudit.Elements.Any(x => string.Equals(x.Status, "conflict", StringComparison.OrdinalIgnoreCase)))
                                {
                                    cellAudit.Status = "conflict";
                                }
                                else if (cellAudit.Elements.Any(x => string.Equals(x.Status, "failed", StringComparison.OrdinalIgnoreCase)))
                                {
                                    cellAudit.Status = "failed";
                                }
                                else if (cellAudit.Elements.Any(x => string.Equals(x.Status, "changed", StringComparison.OrdinalIgnoreCase)))
                                {
                                    cellAudit.Status = "changed";
                                }
                                else if (cellAudit.Elements.Any(x => string.Equals(x.Status, "unchanged", StringComparison.OrdinalIgnoreCase)))
                                {
                                    cellAudit.Status = "unchanged";
                                }
                                else
                                {
                                    cellAudit.Status = "skipped";
                                }

                                rowAudit.Cells.Add(cellAudit);
                            }

                            updatedCount += rowUpdated;
                            skippedCount += rowSkipped;
                            failedCount += rowFailed;
                            rowAudit.Updated = rowUpdated;
                            rowAudit.Skipped = rowSkipped;
                            rowAudit.Failed = rowFailed;
                            rowAudit.Message = string.Join(" | ", rowMessages);
                            auditRows.Add(rowAudit);
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
                        File.WriteAllText(auditJsonPath, JsonConvert.SerializeObject(new
                        {
                            ok = false,
                            filePath,
                            reportPath,
                            auditJsonPath,
                            docTitle = doc.Title,
                            docGuid = p.Value<string>("docGuid") ?? string.Empty,
                            mode = exportMode,
                            updatedCount,
                            changedCount,
                            unchangedCount,
                            skippedCount,
                            failedCount,
                            conflictCount,
                            changes = auditChanges,
                            rows = auditRows
                        }, Formatting.Indented));
                        return new
                        {
                            ok = false,
                            msg = "Display mode import mapping failed.",
                            detail = "display export rows could not be mapped back to element ids. Re-export with the latest addin.",
                            path = filePath,
                            reportPath,
                            auditJsonPath,
                            mode = exportMode,
                            schemaVersion,
                            rowResolutionMode = rowTokenAuthoritative
                                ? ScheduleRoundtripExcelUtil.RowResolutionModeAuthoritative
                                : "legacy-fallback",
                            updatedCount,
                            changedCount,
                            unchangedCount,
                            skippedCount,
                            failedCount,
                            conflictCount,
                            editableColumnCount = cols.Count
                        };
                    }

                    File.WriteAllLines(reportPath, reportRows);
                    File.WriteAllText(auditJsonPath, JsonConvert.SerializeObject(new
                    {
                        ok = true,
                        filePath,
                        reportPath,
                        auditJsonPath,
                        docTitle = doc.Title,
                        docGuid = p.Value<string>("docGuid") ?? string.Empty,
                        mode = exportMode,
                        updatedCount,
                        changedCount,
                        unchangedCount,
                        skippedCount,
                        failedCount,
                        conflictCount,
                        changes = auditChanges,
                        rows = auditRows,
                        generatedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                        editorUser = Environment.UserName,
                        machineName = Environment.MachineName
                    }, Formatting.Indented));

                    return new
                    {
                        ok = true,
                        path = filePath,
                        reportPath,
                        auditJsonPath,
                        mode = exportMode,
                        schemaVersion,
                        rowResolutionMode = rowTokenAuthoritative
                            ? ScheduleRoundtripExcelUtil.RowResolutionModeAuthoritative
                            : "legacy-fallback",
                        updatedCount,
                        changedCount,
                        unchangedCount,
                        skippedCount,
                        failedCount,
                        conflictCount,
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

    public class PreviewScheduleRoundtripExcelCommand : IRevitCommandHandler
    {
        public string CommandName => "preview_schedule_roundtrip_excel";

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

            try
            {
                using (var wb = new XLWorkbook(filePath, XLEventTracking.Disabled))
                {
                    var dataSheetName = p.Value<string>("sheetName") ?? ScheduleRoundtripExcelUtil.DataSheetName;
                    var ws = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, dataSheetName, StringComparison.OrdinalIgnoreCase))
                             ?? wb.Worksheets.FirstOrDefault(x => !string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.RowMapSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.BaselineSheetName, StringComparison.OrdinalIgnoreCase)
                                                               && !string.Equals(x.Name, ScheduleRoundtripExcelUtil.ReadmeSheetName, StringComparison.OrdinalIgnoreCase));
                    if (ws == null)
                        return new { ok = false, msg = "Data sheet not found in workbook." };

                    var meta = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.MetaSheetName, StringComparison.OrdinalIgnoreCase));
                    if (meta == null)
                        return new { ok = false, msg = "Metadata sheet not found. Use export_schedule_roundtrip_excel output." };
                    var allCols = ScheduleRoundtripExcelUtil.ReadMetaSheet(meta).ToList();
                    var cols = allCols.Where(c => c.Editable).ToList();
                    if (cols.Count == 0)
                        return new { ok = false, msg = "No editable columns found in metadata." };
                    var baselineSheet = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.BaselineSheetName, StringComparison.OrdinalIgnoreCase));
                    if (baselineSheet == null)
                        return new { ok = false, msg = "Baseline snapshot sheet not found. Re-export the schedule with the current build before previewing." };
                    if (!ScheduleRoundtripExcelUtil.ValidateWorkbookDocumentScope(doc, meta, out var workbookScopeMessage))
                        return new { ok = false, msg = workbookScopeMessage };
                    var schemaVersion = ScheduleRoundtripExcelUtil.GetWorkbookSchemaVersion(meta);
                    bool rowTokenAuthoritative = ScheduleRoundtripExcelUtil.IsRowTokenAuthoritativeWorkbook(meta);
                    if (!ScheduleRoundtripExcelUtil.ValidateWorkbookLayout(meta, ws, baselineSheet, allCols, out var workbookLayoutMessage))
                        return new { ok = false, msg = workbookLayoutMessage };
                    var baselineRows = ScheduleRoundtripExcelUtil.BuildBaselineRows(baselineSheet, allCols);
                    var baselineRowTokenLookup = ScheduleRoundtripExcelUtil.BuildBaselineRowTokenLookup(baselineRows);
                    var baselineRowIdentityLookup = ScheduleRoundtripExcelUtil.BuildBaselineRowIdentityLookup(baselineRows);
                    var baselineHiddenIdLookup = ScheduleRoundtripExcelUtil.BuildBaselineHiddenIdLookup(baselineRows);
                    var baselineVisibleRowLookup = ScheduleRoundtripExcelUtil.BuildBaselineVisibleRowLookup(baselineRows);
                    var rowMapSheet = wb.Worksheets.FirstOrDefault(x => string.Equals(x.Name, ScheduleRoundtripExcelUtil.RowMapSheetName, StringComparison.OrdinalIgnoreCase));
                    var rowMap = ScheduleRoundtripExcelUtil.ReadRowMapSheet(rowMapSheet);
                    var rowMapEntriesByToken = rowTokenAuthoritative
                        ? ScheduleRoundtripExcelUtil.ReadRowMapEntriesByToken(rowMapSheet)
                        : new Dictionary<string, ScheduleRowMapEntry>(StringComparer.OrdinalIgnoreCase);

                    var exportMode = meta.Cell(4, 2).GetString();
                    bool allowHeuristicDisplayWriteRemap =
                        !rowTokenAuthoritative
                        && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase);
                    ViewSchedule? displaySchedule = null;
                    ElementId tempId = ElementId.InvalidElementId;
                    IList<ScheduleRoundtripColumn> displayCols = Array.Empty<ScheduleRoundtripColumn>();
                    IList<(int ElementId, IList<string> Values)> itemizedDisplayRows = Array.Empty<(int ElementId, IList<string> Values)>();
                    IDictionary<string, int> worksheetHeaderColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    IDictionary<string, int> baselineHeaderColumnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    IDictionary<int, List<int>> baselineSequentialRowElementMap = new Dictionary<int, List<int>>();
                    IDictionary<string, List<int>> identityValueElementMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    IList<int> displayKeyIndexes = Array.Empty<int>();

                    try
                    {
                        if (allowHeuristicDisplayWriteRemap
                            && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase))
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
                                displayKeyIndexes = ScheduleRoundtripExcelUtil.GetDisplayKeyColumnIndexes(displaySchedule, displayCols);
                                identityValueElementMap = ScheduleRoundtripExcelUtil.BuildIdentityValueElementMap(doc, mappingRowElementIds, displayCols);
                                itemizedDisplayRows = ScheduleRoundtripExcelUtil.BuildItemizedDisplayRows(doc, mappingWork, displayCols, mappingRowElementIds);
                                worksheetHeaderColumnMap = ScheduleRoundtripExcelUtil.BuildWorksheetHeaderColumnMap(ws);
                                baselineHeaderColumnMap = ScheduleRoundtripExcelUtil.BuildWorksheetHeaderColumnMap(baselineSheet);
                                baselineSequentialRowElementMap = ScheduleRoundtripExcelUtil.BuildSequentialBaselineRowElementMap(
                                    baselineSheet,
                                    displayCols,
                                    baselineHeaderColumnMap,
                                    itemizedDisplayRows,
                                    ScheduleRoundtripExcelUtil.IsScheduleItemized(displaySchedule),
                                    displayKeyIndexes);
                            }
                        }
                    }
                    catch { }

                    var rows = new List<object>();
                    int changedCellCount = 0;
                    int unchangedCellCount = 0;
                    int skippedCellCount = 0;
                    int failedCellCount = 0;
                    int conflictCellCount = 0;
                    var workbookTargetRequests = new Dictionary<string, ScheduleTargetRequest>(StringComparer.OrdinalIgnoreCase);
                    var usedDisplayFallbackElementIds = new HashSet<int>();
                    int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                    for (int row = 2; row <= lastRow; row++)
                    {
                        var baselineResolution = ScheduleRoundtripExcelUtil.ResolveBaselineRow(
                            ws,
                            allCols,
                            baselineRows,
                            baselineRowTokenLookup,
                            baselineRowIdentityLookup,
                            baselineHiddenIdLookup,
                            baselineVisibleRowLookup,
                            row,
                            allowVisibleFallback: false);
                        var baselineRow = baselineResolution.RowNumber;
                        var editedCols = cols
                            .Where(col =>
                            {
                                var text = ws.Cell(row, col.OutputColumnNumber).GetString();
                                return !string.IsNullOrWhiteSpace(text)
                                       && ScheduleRoundtripExcelUtil.IsWorkbookCellEdited(ws, baselineSheet, row, baselineRow, col.OutputColumnNumber);
                            })
                            .ToList();
                        if (editedCols.Count == 0)
                            continue;

                        List<int> rowElementIds;
                        string mappingSource;
                        bool stableBaselineResolution =
                            !string.Equals(baselineResolution.Source, "row-number", StringComparison.OrdinalIgnoreCase);
                        if (rowTokenAuthoritative)
                        {
                            if (ScheduleRoundtripExcelUtil.TryGetWorksheetRowMapEntryByToken(
                                ws,
                                rowMapEntriesByToken,
                                row,
                                out var rowEntry,
                                out mappingSource)
                                && rowEntry != null)
                            {
                                rowElementIds = (rowEntry.ElementIds ?? new List<int>())
                                    .Where(id => id > 0)
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();
                            }
                            else
                            {
                                rowElementIds = new List<int>();
                            }
                        }
                        else
                        {
                            rowElementIds = ScheduleRoundtripExcelUtil.ResolveWorksheetRowElementIds(
                                ws,
                                baselineSheet,
                                rowMap,
                                row,
                                baselineResolution).ToList();
                            mappingSource = baselineResolution.Source;
                            if (string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(mappingSource, "row-number", StringComparison.OrdinalIgnoreCase))
                            {
                                rowElementIds.Clear();
                                mappingSource = "row-identity-missing";
                            }
                            if (string.Equals(mappingSource, "row-number", StringComparison.OrdinalIgnoreCase))
                                mappingSource = rowElementIds.Count > 0 ? "worksheet-hidden-id-column" : string.Empty;
                            if (rowElementIds.Count == 0
                                && stableBaselineResolution
                                && allowHeuristicDisplayWriteRemap
                                && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                && baselineSequentialRowElementMap.TryGetValue(baselineRow, out var sequentialIds)
                                && sequentialIds != null
                                && sequentialIds.Count > 0)
                            {
                                rowElementIds = sequentialIds
                                    .Where(id => id > 0)
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();
                                if (rowElementIds.Count > 0)
                                    mappingSource = "baseline-helper-sequence";
                            }
                            if (rowElementIds.Count == 0
                                && allowHeuristicDisplayWriteRemap
                                && stableBaselineResolution
                                && string.Equals(exportMode, ScheduleRoundtripExcelUtil.ExportModeDisplay, StringComparison.OrdinalIgnoreCase)
                                && displayCols.Count > 0
                                && itemizedDisplayRows.Count > 0)
                            {
                                var displayRowValues = ScheduleRoundtripExcelUtil.ReadWorksheetDisplayRowValues(
                                    baselineSheet,
                                    baselineRow,
                                    displayCols,
                                    baselineHeaderColumnMap);
                                if (ScheduleRoundtripExcelUtil.IsLikelyDisplayNonElementRow(displayRowValues, displayCols, displayKeyIndexes))
                                    displayRowValues = Array.Empty<string>();
                                rowElementIds = ScheduleRoundtripExcelUtil
                                    .MatchDisplayRowToLiveElementIds(displayRowValues, displayCols, identityValueElementMap, itemizedDisplayRows)
                                    .ToList();
                                rowElementIds = rowElementIds
                                    .Where(id => id > 0 && !usedDisplayFallbackElementIds.Contains(id))
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();
                                if (rowElementIds.Count > 0)
                                    mappingSource = "baseline-display-live-match";
                            }
                        }
                        foreach (var id in rowElementIds)
                            usedDisplayFallbackElementIds.Add(id);

                        var rowLabel = ScheduleRoundtripExcelUtil.BuildWorksheetRowLabel(ws, row, cols);
                        if (rowElementIds.Count == 0)
                        {
                            rows.Add(new
                            {
                                row,
                                label = rowLabel,
                                mappingSource = string.IsNullOrWhiteSpace(mappingSource) ? "unmapped" : mappingSource,
                                elementIds = Array.Empty<int>(),
                                status = "UNMAPPED",
                                message = "missing-element-id",
                                cells = Array.Empty<object>()
                            });
                            skippedCellCount++;
                            continue;
                        }

                        var cellResults = new List<object>();
                        var rowHasChanged = false;
                        foreach (var col in editedCols)
                        {
                            var cellText = ws.Cell(row, col.OutputColumnNumber).GetString();
                            if (string.IsNullOrWhiteSpace(cellText))
                                continue;

                            var elementResults = new List<object>();
                            var cellStatus = "UNCHANGED";
                            var cellMessage = string.Empty;

                            var processedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var eid in rowElementIds)
                            {
                                var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                                if (element == null)
                                {
                                    elementResults.Add(new { elementId = eid, status = "FAILED", before = string.Empty, imported = cellText, after = string.Empty, message = "element-not-found" });
                                    cellStatus = "FAILED";
                                    cellMessage = "element-not-found";
                                    failedCellCount++;
                                    continue;
                                }

                                var prm = ScheduleRoundtripExcelUtil.ResolveParameterByFixedScope(element, col, out var owner, out var scope);
                                if (prm == null)
                                {
                                    elementResults.Add(new { elementId = eid, status = "SKIPPED", before = string.Empty, imported = cellText, after = string.Empty, message = "parameter-not-found" });
                                    if (!string.Equals(cellStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
                                        cellStatus = "SKIPPED";
                                    skippedCellCount++;
                                    continue;
                                }

                                var resolvedTargetKey = ScheduleRoundtripExcelUtil.BuildResolvedTargetKey(owner, scope, eid);
                                if (!processedTargets.Add(resolvedTargetKey))
                                    continue;

                                var targetElementId = ScheduleRoundtripExcelUtil.GetResolvedTargetElementId(owner, eid);
                                var valueOwner = owner ?? element;

                                if (prm.IsReadOnly)
                                {
                                    elementResults.Add(new { elementId = targetElementId, status = "SKIPPED", before = string.Empty, imported = cellText, after = string.Empty, message = "parameter-readonly" });
                                    if (!string.Equals(cellStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
                                        cellStatus = "SKIPPED";
                                    skippedCellCount++;
                                    continue;
                                }

                                var beforeValue = ScheduleRoundtripExcelUtil.GetExportValueForColumn(valueOwner, col, prm.AsValueString() ?? prm.AsString() ?? string.Empty);
                                if (!ScheduleRoundtripExcelUtil.TryNormalizeImportedPreviewValue(prm, col, cellText, out var normalizedDisplay, out var importedComparable, out var msg))
                                {
                                    elementResults.Add(new { elementId = targetElementId, status = "FAILED", before = beforeValue, imported = cellText, after = beforeValue, message = msg });
                                    cellStatus = "FAILED";
                                    cellMessage = msg;
                                    failedCellCount++;
                                    continue;
                                }

                                var targetRequestKey = ScheduleRoundtripExcelUtil.BuildTargetRequestKey(col.OutputColumnNumber, targetElementId);
                                if (workbookTargetRequests.TryGetValue(targetRequestKey, out var existingTargetRequest))
                                {
                                    if (!ScheduleRoundtripExcelUtil.ExportValuesEqual(existingTargetRequest.ImportedComparable, importedComparable)
                                        && !ScheduleRoundtripExcelUtil.ExportValuesEqual(existingTargetRequest.ImportedDisplay, normalizedDisplay))
                                    {
                                        elementResults.Add(new
                                        {
                                            elementId = targetElementId,
                                            status = "CONFLICT",
                                            baseline = string.Empty,
                                            before = beforeValue,
                                            beforeComparable = string.Empty,
                                            imported = normalizedDisplay,
                                            importedComparable,
                                            after = beforeValue,
                                            canApply = false,
                                            message = $"same-target-edited-with-different-values(row-{existingTargetRequest.Row})"
                                        });
                                        cellStatus = "CONFLICT";
                                        rowHasChanged = true;
                                        conflictCellCount++;
                                        continue;
                                    }

                                    elementResults.Add(new
                                    {
                                        elementId = targetElementId,
                                        status = "UNCHANGED",
                                        baseline = string.Empty,
                                        before = beforeValue,
                                        beforeComparable = string.Empty,
                                        imported = normalizedDisplay,
                                        importedComparable,
                                        after = normalizedDisplay,
                                        canApply = true,
                                        message = $"same-target-already-accounted-for(row-{existingTargetRequest.Row})"
                                    });
                                    if (!string.Equals(cellStatus, "CHANGED", StringComparison.OrdinalIgnoreCase)
                                        && !string.Equals(cellStatus, "CONFLICT", StringComparison.OrdinalIgnoreCase))
                                    {
                                        unchangedCellCount++;
                                    }
                                    continue;
                                }

                                workbookTargetRequests[targetRequestKey] = new ScheduleTargetRequest
                                {
                                    Row = row,
                                    OutputColumnNumber = col.OutputColumnNumber,
                                    TargetElementId = targetElementId,
                                    Scope = scope ?? string.Empty,
                                    ImportedComparable = importedComparable,
                                    ImportedDisplay = normalizedDisplay
                                };

                                var beforeComparable = ScheduleRoundtripExcelUtil.GetPreviewComparableValue(prm, col);
                                var baselineDisplay = baselineSheet.Cell(baselineRow, col.OutputColumnNumber).GetString();
                                var baselineComparable = ScheduleRoundtripExcelUtil.GetComparableValueFromWorksheetText(prm, col, baselineDisplay);
                                var sameAsCurrent = ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeComparable, importedComparable)
                                                 || ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeValue, normalizedDisplay);
                                var liveChangedSinceExport = !ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeComparable, baselineComparable)
                                                          && !ScheduleRoundtripExcelUtil.ExportValuesEqual(beforeValue, baselineDisplay);
                                var status = sameAsCurrent
                                    ? "UNCHANGED"
                                    : liveChangedSinceExport
                                        ? "CONFLICT"
                                        : "CHANGED";
                                elementResults.Add(new
                                {
                                    elementId = targetElementId,
                                    status,
                                    baseline = baselineDisplay,
                                    before = beforeValue,
                                    beforeComparable,
                                    imported = normalizedDisplay,
                                    importedComparable,
                                    after = normalizedDisplay,
                                    canApply = !string.Equals(status, "CONFLICT", StringComparison.OrdinalIgnoreCase),
                                    message = string.Equals(status, "CONFLICT", StringComparison.OrdinalIgnoreCase)
                                        ? "live-value-changed-since-export"
                                        : string.Equals(status, "CHANGED", StringComparison.OrdinalIgnoreCase)
                                            ? "will-update"
                                            : "no-effective-change"
                                });

                                if (string.Equals(status, "CHANGED", StringComparison.OrdinalIgnoreCase))
                                {
                                    cellStatus = "CHANGED";
                                    rowHasChanged = true;
                                    changedCellCount++;
                                }
                                else if (string.Equals(status, "CONFLICT", StringComparison.OrdinalIgnoreCase))
                                {
                                    cellStatus = "CONFLICT";
                                    rowHasChanged = true;
                                    conflictCellCount++;
                                }
                                else if (!string.Equals(cellStatus, "CHANGED", StringComparison.OrdinalIgnoreCase))
                                {
                                    unchangedCellCount++;
                                }
                            }

                            if (elementResults.Count == 0)
                                continue;

                            cellResults.Add(new
                            {
                                outputColumnNumber = col.OutputColumnNumber,
                                header = col.Header,
                                parameterName = col.ParamName,
                                editable = col.Editable,
                                isBoolean = col.IsBoolean,
                                resolvedScope = ScheduleRoundtripExcelUtil.NormalizeResolvedScope(col.ResolvedScope),
                                importedValue = cellText,
                                status = cellStatus,
                                message = cellMessage,
                                elements = elementResults
                            });
                        }

                        var rowStatus = rowHasChanged ? "CHANGED" : "UNCHANGED";

                        rows.Add(new
                        {
                            row,
                            label = rowLabel,
                            mappingSource = string.IsNullOrWhiteSpace(mappingSource) ? "worksheet-row-map" : mappingSource,
                            elementIds = rowElementIds,
                            status = rowStatus,
                            message = string.Empty,
                            cells = cellResults
                        });
                    }

                    ScheduleRoundtripExcelUtil.CleanupTempSchedule(doc, tempId);

                    return new
                    {
                        ok = true,
                        path = filePath,
                        mode = exportMode,
                        schemaVersion,
                        rowResolutionMode = rowTokenAuthoritative
                            ? ScheduleRoundtripExcelUtil.RowResolutionModeAuthoritative
                            : "legacy-fallback",
                        scheduleViewId = meta.Cell(2, 2).GetString(),
                        scheduleName = meta.Cell(3, 2).GetString(),
                        editableColumnCount = cols.Count,
                        changedCellCount,
                        conflictCellCount,
                        unchangedCellCount,
                        skippedCellCount,
                        failedCellCount,
                        rows
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Schedule roundtrip preview failed.", detail = ex.Message };
            }
        }
    }
}

