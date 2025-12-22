// ================================================================
// File: Commands/VisualizationOps/ColorFillCommands.cs
// Revit 2023+ / .NET Framework 4.8
// 概要:
//  - list_view_colorfill_categories
//  - list_colorfill_supported_params
//  - list_color_schemes
//  - list_colorfill_builtin_param_suggestions   ← 新規
//  - apply_quick_color_scheme
// 依存: Newtonsoft.Json.Linq, RevitMCPAddin.Core (RequestCommand, IRevitCommandHandler など)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    internal static class CfUtil
    {
        public static ElementId GetSolidDraftingFillId(Document doc)
        {
            var fps = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .ToList();

            var solid = fps.FirstOrDefault(fp =>
            {
                var p = fp.GetFillPattern();
                return p != null && p.Target == FillPatternTarget.Drafting && p.IsSolidFill;
            });
            return solid != null ? solid.Id : ElementId.InvalidElementId;
        }

        public static BuiltInCategory? AsBic(ElementId catId)
        {
            try { return (BuiltInCategory)catId.IntValue(); }
            catch { return null; }
        }

        public static Color Rgb(int r, int g, int b) => new Color((byte)r, (byte)g, (byte)b);

        public static bool TryParseBuiltInParamName(string name, out BuiltInParameter bip)
        {
            bip = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var alias = name.Trim().ToUpperInvariant();

            // --- 互換エイリアス（ユーザー/エージェントが使いがちな名称）---
            // Spaces / Areas は ROOM_* を使用（AREA_* / SPACE_* は列挙に存在しない）
            switch (alias)
            {
                case "SPACE_NAME": alias = "ROOM_NAME"; break;
                case "SPACE_NUMBER": alias = "ROOM_NUMBER"; break;
                case "SPACE_AREA": alias = "ROOM_AREA"; break;

                case "AREA_NAME": alias = "ROOM_NAME"; break;
                case "AREA": alias = "ROOM_AREA"; break;
                case "AREA_PERIMETER": alias = "ROOM_PERIMETER"; break;
            }

            return Enum.TryParse(alias, ignoreCase: true, out bip);
        }

        // ---- ColorBrewer系 推奨パレット（代表） ----
        public static readonly Dictionary<string, List<Color>> Brewer = new Dictionary<string, List<Color>>(StringComparer.OrdinalIgnoreCase)
        {
            // Qualitative
            ["Set3"] = new List<Color> {
                Rgb(141,211,199), Rgb(255,255,179), Rgb(190,186,218), Rgb(251,128,114),
                Rgb(128,177,211), Rgb(253,180,98),  Rgb(179,222,105), Rgb(252,205,229),
                Rgb(217,217,217), Rgb(188,128,189), Rgb(204,235,197), Rgb(255,237,111)
            },
            ["Paired"] = new List<Color> {
                Rgb(166,206,227), Rgb(31,120,180),  Rgb(178,223,138), Rgb(51,160,44),
                Rgb(251,154,153), Rgb(227,26,28),   Rgb(253,191,111), Rgb(255,127,0),
                Rgb(202,178,214), Rgb(106,61,154),  Rgb(255,255,153), Rgb(177,89,40)
            },
            ["Dark2"] = new List<Color> {
                Rgb(27,158,119), Rgb(217,95,2),  Rgb(117,112,179), Rgb(231,41,138),
                Rgb(102,166,30), Rgb(230,171,2), Rgb(166,118,29),  Rgb(102,102,102)
            },
            // Sequential
            ["YlOrRd"] = new List<Color> {
                Rgb(255,255,204), Rgb(255,237,160), Rgb(254,217,118), Rgb(254,178,76),
                Rgb(253,141,60),  Rgb(252,78,42),   Rgb(227,26,28),   Rgb(177,0,38), Rgb(128,0,38)
            },
            ["BuGn"] = new List<Color> {
                Rgb(237,248,251), Rgb(204,236,230), Rgb(153,216,201), Rgb(102,194,164),
                Rgb(65,174,118),  Rgb(35,139,69),   Rgb(0,109,44),    Rgb(0,68,27),  Rgb(0,45,20)
            },
            // Diverging
            ["RdYlBu"] = new List<Color> {
                Rgb(215,48,39), Rgb(244,109,67), Rgb(253,174,97), Rgb(254,224,144),
                Rgb(255,255,191), Rgb(224,243,248), Rgb(171,217,233), Rgb(116,173,209),
                Rgb(69,117,180), Rgb(49,54,149), Rgb(39,26,123)
            },
            ["Spectral"] = new List<Color> {
                Rgb(158,1,66), Rgb(213,62,79), Rgb(244,109,67), Rgb(253,174,97),
                Rgb(254,224,139), Rgb(230,245,152), Rgb(171,221,164), Rgb(102,194,165),
                Rgb(50,136,189), Rgb(94,79,162), Rgb(73,37,125)
            }
        };

        // カテゴリごとの推奨BuiltInParameter集合（IDと名前）
        public static IReadOnlyList<(int id, string name, string note)> SuggestionsFor(BuiltInCategory bic)
        {
            var list = new List<(int, string, string)>();
            switch (bic)
            {
                case BuiltInCategory.OST_Areas:
                    // ※ Areas でも ROOM_* 系 BIP を使う
                    list.Add(((int)BuiltInParameter.ROOM_NAME, "ROOM_NAME", "エリア名（質的）"));
                    list.Add(((int)BuiltInParameter.ROOM_AREA, "ROOM_AREA", "面積（数値）"));
                    list.Add(((int)BuiltInParameter.ROOM_PERIMETER, "ROOM_PERIMETER", "周長（数値）"));
                    break;

                case BuiltInCategory.OST_Rooms:
                    list.Add(((int)BuiltInParameter.ROOM_NAME, "ROOM_NAME", "部屋名（質的）"));
                    list.Add(((int)BuiltInParameter.ROOM_DEPARTMENT, "ROOM_DEPARTMENT", "部署（質的）"));
                    list.Add(((int)BuiltInParameter.ROOM_AREA, "ROOM_AREA", "面積（数値）"));
                    break;

                case BuiltInCategory.OST_MEPSpaces:
                    // ※ Space は ROOM_* を使用（SPACE_* は存在しない）
                    list.Add(((int)BuiltInParameter.ROOM_NAME, "ROOM_NAME", "スペース名（質的）"));
                    list.Add(((int)BuiltInParameter.ROOM_NUMBER, "ROOM_NUMBER", "スペース番号（質的）"));
                    list.Add(((int)BuiltInParameter.ROOM_AREA, "ROOM_AREA", "面積（数値）"));
                    break;

                case BuiltInCategory.OST_DuctCurves:
                    list.Add(((int)BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, "RBS_CURVE_DIAMETER_PARAM", "ダクト径（数値）"));
                    list.Add(((int)BuiltInParameter.RBS_VELOCITY, "RBS_VELOCITY", "風速（数値）"));
                    break;

                case BuiltInCategory.OST_PipeCurves:
                    list.Add(((int)BuiltInParameter.RBS_PIPE_DIAMETER_PARAM, "RBS_PIPE_DIAMETER_PARAM", "管径（数値）"));
                    list.Add(((int)BuiltInParameter.RBS_VELOCITY, "RBS_VELOCITY", "流速（数値）"));
                    break;

                default:
                    break;
            }
            return list;
        }

        // 入力（paramDefinitionId / builtInParamId / builtInParamName）から ElementId を解決し、カテゴリに妥当か軽く検証
        public static (bool ok, ElementId id, string message) ResolveParamIdForCategory(JObject p, BuiltInCategory bic)
        {
            ElementId id = ElementId.InvalidElementId;

            if (p.TryGetValue("paramDefinitionId", out var pdTok))
                id = Autodesk.Revit.DB.ElementIdCompat.From(pdTok.Value<int>());
            else if (p.TryGetValue("builtInParamId", out var bipTok))
                id = Autodesk.Revit.DB.ElementIdCompat.From(bipTok.Value<int>());
            else if (p.TryGetValue("builtInParamName", out var bipNameTok))
            {
                if (TryParseBuiltInParamName(bipNameTok.Value<string>(), out var bipEnum))
                    id = Autodesk.Revit.DB.ElementIdCompat.From((int)bipEnum);
                else
                    return (false, id, "Unknown builtInParamName. Use list_colorfill_builtin_param_suggestions.");
            }

            if (id == ElementId.InvalidElementId)
                return (false, id, "Specify one of: paramDefinitionId | builtInParamId | builtInParamName.");

            // 軽い妥当性チェック：推奨集合に含まれているか（含まれていなくても Revit 側で有効ならOKなので強制はしない）
            var sug = SuggestionsFor(bic);
            if (sug.Count > 0 && !sug.Any(x => x.id == id.IntValue()))
            {
                // 参考メッセージだけ与える（ブロックはしない）
                return (true, id, "Note: parameter is not in the suggested list for this category.");
            }
            return (true, id, null);
        }
    }

    // ----------------------------------------------------------------
    // 1) ビューでカラーフィル対応カテゴリ一覧
    // ----------------------------------------------------------------
    public class ListViewColorFillCategoriesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_view_colorfill_categories";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            int viewIdInt = p.TryGetValue("viewId", out var v) ? v.Value<int>() : uiapp.ActiveUIDocument.ActiveView.Id.IntValue();
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as View;
            if (view == null) return new { ok = false, message = $"View not found: {viewIdInt}" };

            var cats = view.SupportedColorFillCategoryIds();
            var result = new List<object>();
            foreach (var id in cats)
            {
                string name = null;
                try
                {
                    var bic = (BuiltInCategory)id.IntValue();
                    name = doc.Settings.Categories.get_Item(bic)?.Name;
                }
                catch { }
                result.Add(new { categoryId = id.IntValue(), name });
            }
            return new { ok = true, viewId = view.Id.IntValue(), categories = result };
        }
    }

    // ----------------------------------------------------------------
    // 2) 既存スキームから参照可能な ParameterDefinitionIds（補助）
    // ----------------------------------------------------------------
    public class ListColorFillSupportedParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_colorfill_supported_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            if (p == null || !p.TryGetValue("categoryId", out var catTok))
                return new { ok = false, message = "Parameter 'categoryId' is required." };
            var catId = Autodesk.Revit.DB.ElementIdCompat.From(catTok.Value<int>());

            var baseScheme = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ColorFillSchema)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .FirstOrDefault(s => s.CategoryId == catId);

            if (baseScheme == null)
                return new { ok = true, categoryId = catId.IntValue(), paramDefinitionIds = new int[0], note = "No existing scheme for this category. Empty is normal." };

            var ids = baseScheme.GetSupportedParameterIds()?.Select(eid => eid.IntValue()).ToArray() ?? new int[0];
            return new { ok = true, categoryId = catId.IntValue(), paramDefinitionIds = ids };
        }
    }

    // ----------------------------------------------------------------
    // 3) 既存スキーム列挙（カテゴリ必須、Area は areaSchemeId で絞込可）
    // ----------------------------------------------------------------
    public class ListColorSchemesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_color_schemes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            if (p == null || !p.TryGetValue("categoryId", out var catTok))
                return new { ok = false, message = "Parameter 'categoryId' is required." };
            var catId = Autodesk.Revit.DB.ElementIdCompat.From(catTok.Value<int>());

            ElementId areaSchemeId = ElementId.InvalidElementId;
            if (p.TryGetValue("areaSchemeId", out var aTok))
                areaSchemeId = Autodesk.Revit.DB.ElementIdCompat.From(aTok.Value<int>());

            var schemes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ColorFillSchema)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .Where(s => s.CategoryId == catId && (areaSchemeId == ElementId.InvalidElementId || s.AreaSchemeId == areaSchemeId))
                .Select(s => new
                {
                    schemeId = s.Id.IntValue(),
                    name = s.Name,
                    paramDefinitionId = s.ParameterDefinition?.IntValue() ?? -1,
                    isByRange = s.IsByRange,
                    entryCount = s.GetEntries()?.Count ?? 0
                })
                .ToList();

            return new { ok = true, schemes };
        }
    }

    // ----------------------------------------------------------------
    // 3.5) 推奨 BuiltInParameter 一覧（カテゴリ別） ← 新規
    // ----------------------------------------------------------------
    public class ListColorfillBuiltinParamSuggestionsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_colorfill_builtin_param_suggestions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params ?? new JObject();
            if (!p.TryGetValue("categoryId", out var catTok))
                return new { ok = false, message = "Parameter 'categoryId' is required." };

            var bic = (BuiltInCategory)catTok.Value<int>();
            var items = CfUtil.SuggestionsFor(bic)
                .Select(x => new { builtInParamId = x.id, builtInParamName = x.name, note = x.note })
                .ToList();

            return new { ok = true, categoryId = (int)bic, suggestions = items };
        }
    }

    // ----------------------------------------------------------------
    // 4) かんたん適用（Duplicate→基準パラメータ設定→配色→ビュー適用→凡例）
    //    - AreaSchemeId は読み取り専用 → 同一 AreaScheme の既存スキームをベースに Duplicate 必須
    //    - paramDefinitionId の代わりに builtInParamId / builtInParamName でも指定可（カテゴリ妥当性メッセージ付）
    //    - ColorFillSchemeEntry は StorageType 必須（範囲モードで使用）
    // ----------------------------------------------------------------
    public class ApplyQuickColorSchemeCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_quick_color_scheme";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            try
            {
                // View 解決（Graphical優先）
                int viewId = p.TryGetValue("viewId", out var v) ? v.Value<int>() : (uidoc.ActiveGraphicalView?.Id.IntValue() ?? uidoc.ActiveView.Id.IntValue());
                var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View
                           ?? (uidoc.ActiveGraphicalView as View)
                           ?? (uidoc.ActiveView as View);
                if (view == null) return new { ok = false, message = $"View not found: {viewId}" };

                var catId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("categoryId"));
                if (!view.SupportedColorFillCategoryIds().Any(x => x.IntValue() == catId.IntValue()))
                    return new { ok = false, message = "This view does not support the specified color fill category." };

                var bic = (BuiltInCategory)catId.IntValue();

                // テンプレート対応：detachViewTemplate / 自動複製（plan系想定）
                bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
                bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true; // テンプレート時はビュー複製で回避
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    if (detachTemplate)
                    {
                        using (var tx0 = new Transaction(doc, "Detach View Template (Quick Color)"))
                        { try { tx0.Start(); view.ViewTemplateId = ElementId.InvalidElementId; tx0.Commit(); } catch { try { tx0.RollBack(); } catch { } } }
                    }
                    else if (autoWorkingView)
                    {
                        try
                        {
                            using (var txd = new Transaction(doc, "Duplicate View (Quick Color)"))
                            {
                                txd.Start();
                                var dupId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                                var dup = doc.GetElement(dupId) as View;
                                if (dup == null) { txd.RollBack(); return new { ok = false, message = "View duplicate failed." }; }
                                dup.ViewTemplateId = ElementId.InvalidElementId;
                                try { dup.Name = (view.Name ?? "View") + " (ColorFill)"; } catch { }
                                txd.Commit();
                                view = dup; viewId = dup.Id.IntValue();
                            }
                        }
                        catch (Exception ex)
                        {
                            return new { ok = false, message = "Template applied and duplicate failed. Use detachViewTemplate:true.", detail = ex.Message };
                        }
                    }
                    else
                    {
                        return new { ok = false, message = "View has a template; set detachViewTemplate:true or autoWorkingView:true." };
                    }
                }

                // Area の場合は AreaSchemeId 必須（ColorFillScheme.AreaSchemeId は読み取り専用）
                ElementId areaSchemeId = ElementId.InvalidElementId;
                bool isAreas = bic == BuiltInCategory.OST_Areas;
                if (isAreas)
                {
                    if (!p.TryGetValue("areaSchemeId", out var aTok))
                        return new { ok = false, message = "Area Plan requires 'areaSchemeId' for color scheme application." };
                    areaSchemeId = Autodesk.Revit.DB.ElementIdCompat.From(aTok.Value<int>());
                }

                var mode = (p.Value<string>("mode") ?? "qualitative").ToLowerInvariant(); // qualitative | sequential | diverging
                var paletteName = p.Value<string>("palette") ?? (mode == "qualitative" ? "Set3" : (mode == "diverging" ? "RdYlBu" : "YlOrRd"));
                int maxClasses = Math.Max(2, p.Value<int?>("maxClasses") ?? 12);
                int bins = Math.Max(3, Math.Min(11, p.Value<int?>("bins") ?? 7));

                bool createLegend = p.Value<bool?>("createLegend") ?? true;
                XYZ legendOrigin = new XYZ(
                    p.SelectToken("legendOrigin.x")?.Value<double>() ?? 0.0,
                    p.SelectToken("legendOrigin.y")?.Value<double>() ?? 0.0,
                    p.SelectToken("legendOrigin.z")?.Value<double>() ?? 0.0
                );

                // ベーススキーム選定（Area は AreaSchemeId 一致が必須）
                var query = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ColorFillSchema)
                    .OfClass(typeof(ColorFillScheme))
                    .Cast<ColorFillScheme>()
                    .Where(s => s.CategoryId.IntValue() == catId.IntValue());

                if (isAreas)
                    query = query.Where(s => s.AreaSchemeId != null && s.AreaSchemeId.IntValue() == areaSchemeId.IntValue());

                var baseScheme = query.FirstOrDefault();
                if (baseScheme == null)
                {
                    var need = isAreas ? $"(AreaSchemeId={areaSchemeId.IntValue()}) " : "";
                    return new
                    {
                        ok = false,
                        message = $"No base color scheme found for category {catId.IntValue()} {need}." +
                                  " Create one manually once (for the desired Area Scheme if Areas) or duplicate an existing matching scheme, then re-run.",
                        hint = "AreaSchemeId on ColorFillScheme is read-only; it must match the base scheme."
                    };
                }

                // Duplicate
                ElementId newSchemeId;
                using (var t = new Transaction(doc, "Duplicate Color Scheme"))
                {
                    t.Start();
                    var newName = $"{baseScheme.Name} - MCP Auto {DateTime.Now:HHmmss}";
                    newSchemeId = baseScheme.Duplicate(newName);
                    t.Commit();
                }
                var scheme = (ColorFillScheme)doc.GetElement(newSchemeId);

                // ParameterDefinition 解決（カテゴリ妥当性メッセージ付）
                var resolved = CfUtil.ResolveParamIdForCategory(p, bic);
                if (!resolved.ok)
                    return new { ok = false, message = resolved.message };
                var paramDefId = resolved.id;

                using (var t = new Transaction(doc, "Setup Color Scheme"))
                {
                    t.Start();

                    if (!scheme.IsValidParameterDefinitionId(paramDefId))
                        return new { ok = false, message = $"ParameterDefinition {paramDefId.IntValue()} is not supported by this scheme." };
                    scheme.ParameterDefinition = paramDefId;

                    var colors = CfUtil.Brewer.ContainsKey(paletteName) ? CfUtil.Brewer[paletteName] : CfUtil.Brewer["Set3"];
                    var solidFillId = CfUtil.GetSolidDraftingFillId(doc);
                    var storage = scheme.StorageType;

                    if (mode == "qualitative")
                    {
                        var current = scheme.GetEntries() ?? new List<ColorFillSchemeEntry>();
                        var entries = new List<ColorFillSchemeEntry>();
                        int n = Math.Max(0, Math.Min(maxClasses, current.Count));
                        for (int i = 0; i < n; i++)
                        {
                            var e = current[i];
                            e.FillPatternId = solidFillId;
                            e.Color = colors[i % colors.Count];
                            entries.Add(e);
                        }
                        if (n > 0) scheme.SetEntries(entries);
                        scheme.IsByRange = false;
                    }
                    else
                    {
                        if (storage != StorageType.Double && storage != StorageType.Integer)
                            return new { ok = false, message = "Range mode requires Double or Integer parameter." };

                        var entries = new List<ColorFillSchemeEntry>();
                        if (storage == StorageType.Double)
                        {
                            double min = -100.0, max = 100.0;
                            for (int i = 0; i < bins; i++)
                            {
                                double low = (i == 0) ? -double.MaxValue : (min + (max - min) * i / bins);
                                var e = new ColorFillSchemeEntry(StorageType.Double);
                                e.SetDoubleValue(low);
                                e.FillPatternId = solidFillId;
                                e.Color = colors[i % colors.Count];
                                entries.Add(e);
                            }
                        }
                        else // Integer
                        {
                            int min = -100, max = 100;
                            for (int i = 0; i < bins; i++)
                            {
                                int low = (i == 0) ? int.MinValue : (int)Math.Round(min + (max - min) * i / (double)bins);
                                var e = new ColorFillSchemeEntry(StorageType.Integer);
                                e.SetIntegerValue(low);
                                e.FillPatternId = solidFillId;
                                e.Color = colors[i % colors.Count];
                                entries.Add(e);
                            }
                        }
                        scheme.SetEntries(entries);
                        scheme.IsByRange = true;
                    }

                    view.SetColorFillSchemeId(catId, scheme.Id);
                    t.Commit();
                }

                // 凡例（任意）
                int legendId = -1;
                if (createLegend)
                {
                    using (var t = new Transaction(doc, "Place Color Fill Legend"))
                    {
                        t.Start();
                        var legend = ColorFillLegend.Create(doc, view.Id, catId, legendOrigin);
                        legendId = legend.Id.IntValue();
                        t.Commit();
                    }
                }

                // 備考（カテゴリ妥当性の注意喚起）
                var note = resolved.message; // null かもしれない

                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    schemeId = newSchemeId.IntValue(),
                    legendId = legendId > 0 ? (int?)legendId : null,
                    appliedTo = new { categoryId = catId.IntValue() },
                    note
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, message = ex.Message };
            }
        }
    }
}


