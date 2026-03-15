#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.Export
{
    internal static class DwgExportSetupUtil
    {
        public static IList<string> ListNames(Document doc)
        {
            try { return ExportDWGSettings.ListNames(doc) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        public static ExportDWGSettings? Find(Document doc, string? name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return null;
            try { return ExportDWGSettings.FindByName(doc, name); }
            catch { return null; }
        }

        public static ExportDWGSettings? GetActive(Document doc)
        {
            try { return ExportDWGSettings.GetActivePredefinedSettings(doc); }
            catch { return null; }
        }

        public static Dictionary<string, object> BuildSummary(Document doc, ExportDWGSettings settings)
        {
            var options = settings.GetDWGExportOptions();
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["settingElementId"] = settings.Id.IntegerValue,
                ["uniqueId"] = settings.UniqueId ?? string.Empty,
                ["name"] = SafeName(settings),
                ["isActive"] = string.Equals(SafeName(settings), SafeName(GetActive(doc)), StringComparison.OrdinalIgnoreCase),
                ["fileVersion"] = SafeEnum(options.FileVersion),
                ["colors"] = SafeEnum(options.Colors),
                ["layerMapping"] = options.LayerMapping ?? string.Empty,
                ["propOverrides"] = SafeEnum(options.PropOverrides),
                ["mergeViews"] = SafeBool(() => options.MergedViews),
                ["sharedCoords"] = SafeBool(() => options.SharedCoords),
                ["exportingAreas"] = SafeBool(() => options.ExportingAreas),
                ["layerCount"] = SafeCount(() => options.GetExportLayerTable()?.GetKeys()),
                ["linetypeCount"] = SafeCount(() => options.GetExportLinetypeTable()?.GetKeys()),
                ["fontCount"] = SafeCount(() => options.GetExportFontTable()?.GetKeys()),
                ["patternCount"] = SafeCount(() => options.GetExportPatternTable()?.GetKeys())
            };
        }

        public static Dictionary<string, object> BuildDetail(Document doc, ExportDWGSettings settings, JObject? parameters)
        {
            var p = parameters ?? new JObject();
            bool includeLayers = p.Value<bool?>("includeLayers") ?? true;
            bool includeLineTypes = p.Value<bool?>("includeLineTypes") ?? true;
            bool includeFonts = p.Value<bool?>("includeFonts") ?? false;
            bool includePatterns = p.Value<bool?>("includePatterns") ?? false;
            string categoryContains = (p.Value<string>("categoryContains") ?? string.Empty).Trim();
            string subCategoryContains = (p.Value<string>("subCategoryContains") ?? string.Empty).Trim();
            int limit = Math.Max(0, p.Value<int?>("limit") ?? 0);

            var options = settings.GetDWGExportOptions();
            var data = BuildSummary(doc, settings);
            data["options"] = BuildOptions(options);

            var tables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (includeLayers)
            {
                tables["layers"] = BuildLayerTable(options, categoryContains, subCategoryContains, limit);
            }
            if (includeLineTypes)
            {
                tables["lineTypes"] = BuildLinetypeTable(options);
            }
            if (includeFonts)
            {
                tables["fonts"] = BuildFontTable(options);
            }
            if (includePatterns)
            {
                tables["patterns"] = BuildPatternTable(options);
            }

            data["tables"] = tables;
            data["filters"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["categoryContains"] = categoryContains,
                ["subCategoryContains"] = subCategoryContains,
                ["limit"] = limit
            };
            return data;
        }

        private static Dictionary<string, object> BuildOptions(DWGExportOptions options)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["fileVersion"] = SafeEnum(options.FileVersion),
                ["mergeViews"] = SafeBool(() => options.MergedViews),
                ["colors"] = SafeEnum(options.Colors),
                ["propOverrides"] = SafeEnum(options.PropOverrides),
                ["layerMapping"] = options.LayerMapping ?? string.Empty,
                ["hatchPatternsFileName"] = options.HatchPatternsFileName ?? string.Empty,
                ["linetypesFileName"] = options.LinetypesFileName ?? string.Empty,
                ["lineScaling"] = SafeEnum(options.LineScaling),
                ["textTreatment"] = SafeEnum(options.TextTreatment),
                ["exportOfSolids"] = SafeEnum(options.ExportOfSolids),
                ["targetUnit"] = SafeEnum(options.TargetUnit),
                ["sharedCoords"] = SafeBool(() => options.SharedCoords),
                ["exportingAreas"] = SafeBool(() => options.ExportingAreas),
                ["ACAObjectPreference"] = SafeEnum(options.ACAPreference),
                ["markNonplotLayers"] = SafeBool(() => options.MarkNonplotLayers),
                ["nonplotSuffix"] = options.NonplotSuffix ?? string.Empty,
                ["useHatchBackgroundColor"] = SafeBool(() => options.UseHatchBackgroundColor),
                ["hatchBackgroundColor"] = BuildColor(SafeEval(() => options.HatchBackgroundColor))
            };
        }

        private static Dictionary<string, object> BuildLayerTable(
            DWGExportOptions options,
            string categoryContains,
            string subCategoryContains,
            int limit)
        {
            var items = new List<Dictionary<string, object>>();
            int totalCount = 0;

            try
            {
                var table = options.GetExportLayerTable();
                foreach (ExportLayerKey key in table.GetKeys())
                {
                    var categoryName = key.CategoryName ?? string.Empty;
                    var subCategoryName = key.SubCategoryName ?? string.Empty;
                    if (!MatchesContains(categoryName, categoryContains)) continue;
                    if (!MatchesContains(subCategoryName, subCategoryContains)) continue;

                    totalCount++;
                    if (limit > 0 && items.Count >= limit) continue;

                    var info = table.GetExportLayerInfo(key);
                    items.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["categoryName"] = categoryName,
                        ["subCategoryName"] = subCategoryName,
                        ["specialType"] = SafeEnum(key.SpecialType),
                        ["categoryType"] = SafeEnum(info.CategoryType),
                        ["layerName"] = info.LayerName ?? string.Empty,
                        ["cutLayerName"] = info.CutLayerName ?? string.Empty,
                        ["colorNumber"] = info.ColorNumber,
                        ["cutColorNumber"] = info.CutColorNumber,
                        ["colorName"] = info.ColorName ?? string.Empty,
                        ["layerModifiers"] = BuildLayerModifiers(info.GetLayerModifiers()),
                        ["cutLayerModifiers"] = BuildLayerModifiers(info.GetCutLayerModifiers())
                    });
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ok"] = false,
                    ["msg"] = ex.Message,
                    ["count"] = 0,
                    ["returned"] = 0,
                    ["items"] = new List<object>()
                };
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = true,
                ["count"] = totalCount,
                ["returned"] = items.Count,
                ["items"] = items
            };
        }

        private static Dictionary<string, object> BuildLinetypeTable(DWGExportOptions options)
        {
            var items = new List<Dictionary<string, object>>();
            try
            {
                var table = options.GetExportLinetypeTable();
                foreach (ExportLinetypeKey key in table.GetKeys())
                {
                    var info = table.GetExportLinetypeInfo(key);
                    items.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["originalLinetypeName"] = key.OriginalLinetypeName ?? string.Empty,
                        ["destinationLinetypeName"] = info.DestinationLinetypeName ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ok"] = false,
                    ["msg"] = ex.Message,
                    ["count"] = 0,
                    ["items"] = new List<object>()
                };
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = true,
                ["count"] = items.Count,
                ["items"] = items.OrderBy(x => x["originalLinetypeName"]).ToList()
            };
        }

        private static Dictionary<string, object> BuildFontTable(DWGExportOptions options)
        {
            var items = new List<Dictionary<string, object>>();
            try
            {
                var table = options.GetExportFontTable();
                foreach (ExportFontKey key in table.GetKeys())
                {
                    var info = table.GetExportFontInfo(key);
                    items.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["originalFontName"] = key.OriginalFontName ?? string.Empty,
                        ["destinationFontName"] = info.DestinationFontName ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ok"] = false,
                    ["msg"] = ex.Message,
                    ["count"] = 0,
                    ["items"] = new List<object>()
                };
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = true,
                ["count"] = items.Count,
                ["items"] = items.OrderBy(x => x["originalFontName"]).ToList()
            };
        }

        private static Dictionary<string, object> BuildPatternTable(DWGExportOptions options)
        {
            var items = new List<Dictionary<string, object>>();
            try
            {
                var table = options.GetExportPatternTable();
                foreach (ExportPatternKey key in table.GetKeys())
                {
                    var info = table.GetExportPatternInfo(key);
                    items.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["originalFillPatternName"] = key.OriginalFillPatternName ?? string.Empty,
                        ["originalFillPatternType"] = SafeEnum(key.OriginalFillPatternType),
                        ["destinationPatternName"] = info.DestinationPatternName ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ok"] = false,
                    ["msg"] = ex.Message,
                    ["count"] = 0,
                    ["items"] = new List<object>()
                };
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["ok"] = true,
                ["count"] = items.Count,
                ["items"] = items.OrderBy(x => x["originalFillPatternName"]).ToList()
            };
        }

        private static List<Dictionary<string, object>> BuildLayerModifiers(IList<LayerModifier> modifiers)
        {
            var items = new List<Dictionary<string, object>>();
            if (modifiers == null) return items;

            foreach (var modifier in modifiers)
            {
                items.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["modifierType"] = SafeEnum(modifier.ModifierType),
                    ["separator"] = modifier.Separator ?? string.Empty
                });
            }
            return items;
        }

        private static Dictionary<string, object> BuildColor(Color? color)
        {
            if (color == null) return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["red"] = color.Red,
                ["green"] = color.Green,
                ["blue"] = color.Blue
            };
        }

        private static T? SafeEval<T>(Func<T> getter) where T : class
        {
            try { return getter(); }
            catch { return null; }
        }

        private static int SafeCount<T>(Func<ICollection<T>?> getter)
        {
            try
            {
                var value = getter();
                return value?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try { return getter(); }
            catch { return false; }
        }

        private static string SafeEnum(Enum value)
        {
            try { return value.ToString(); }
            catch { return string.Empty; }
        }

        private static string SafeName(Element? element)
        {
            try { return element?.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool MatchesContains(string source, string contains)
        {
            if (string.IsNullOrWhiteSpace(contains)) return true;
            return (source ?? string.Empty).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class ListDwgExportSetupsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_dwg_export_setups";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd?.Params as JObject ?? new JObject();
            bool detail = p.Value<bool?>("detail") ?? false;

            var active = DwgExportSetupUtil.GetActive(doc);
            var activeName = active?.Name ?? string.Empty;
            var names = DwgExportSetupUtil.ListNames(doc)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var items = new List<object>();
            foreach (var name in names)
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = name,
                    ["isActive"] = string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase)
                };

                if (detail)
                {
                    var settings = DwgExportSetupUtil.Find(doc, name);
                    if (settings != null)
                    {
                        row["summary"] = DwgExportSetupUtil.BuildSummary(doc, settings);
                    }
                }

                items.Add(row);
            }

            return new
            {
                ok = true,
                count = items.Count,
                activeName,
                setups = items
            };
        }
    }

    public class GetDwgExportSetupCommand : IRevitCommandHandler
    {
        public string CommandName => "get_dwg_export_setup";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd?.Params as JObject ?? new JObject();
            var name = (p.Value<string>("name") ?? string.Empty).Trim();

            ExportDWGSettings? settings = !string.IsNullOrWhiteSpace(name)
                ? DwgExportSetupUtil.Find(doc, name)
                : DwgExportSetupUtil.GetActive(doc);

            if (settings == null)
            {
                return new
                {
                    ok = false,
                    code = "NOT_FOUND",
                    msg = string.IsNullOrWhiteSpace(name)
                        ? "アクティブな DWG 出力設定が見つかりません。"
                        : $"DWG 出力設定 '{name}' が見つかりません。"
                };
            }

            var detail = DwgExportSetupUtil.BuildDetail(doc, settings, p);
            detail["ok"] = true;
            return detail;
        }
    }
}
