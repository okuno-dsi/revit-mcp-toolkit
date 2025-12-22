#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // UnitHelper, ResultUtil など共通

namespace RevitMCPAddin.Commands.Visualization
{
    /// <summary>
    /// JSON-RPC:
    ///   - apply_conditional_coloring  (mode: "by_element_ranges" | "colorfill")
    ///   - clear_conditional_coloring
    /// </summary>
    public class ApplyConditionalColoringCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_conditional_coloring";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject?)cmd.Params ?? new JObject();
            var mode = p.Value<string>("mode") ?? "by_element_ranges";

            try
            {
                if (mode == "by_element_ranges")
                    return ApplyByElementRanges(uiapp, doc, p);

                if (mode == "colorfill")
                    return ForwardToColorFill(uiapp, doc, p);

                if (mode == "categorical")
                    return ApplyCategorical(uiapp, doc, p);

                return ResultUtil.Err($"未知のmodeです: {mode}");
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"apply_conditional_coloring 失敗: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // 1) しきい値分類 → 要素オーバーライド
        // ------------------------------------------------------------
        private object ApplyByElementRanges(UIApplication uiapp, Document doc, JObject p)
        {
            // ビュー解決（viewId 未指定/0 → アクティブグラフィックビュー）
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            View view = null;
            if (reqViewId > 0)
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(reqViewId)) as View;
            if (view == null)
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);

            if (view == null) return ResultUtil.Err("viewId が不正、またはアクティブビューを取得できません。");

            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true;
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (detachTemplate)
                {
                    using (var tx0 = new Transaction(doc, "Detach View Template (ApplyConditionalColoring)"))
                    { try { tx0.Start(); view.ViewTemplateId = ElementId.InvalidElementId; tx0.Commit(); } catch { try { tx0.RollBack(); } catch { } } }
                }
                else if (autoWorkingView)
                {
                    using (var tx = new Transaction(doc, "Create Working 3D (ConditionalColoring)"))
                    {
                        try
                        {
                            tx.Start();
                            var vtf = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                            if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                            var v3d = View3D.CreateIsometric(doc, vtf.Id);
                            v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                            tx.Commit();
                            view = v3d;
                            try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            return ResultUtil.Err("ビューにView Templateが適用されています。デタッチするか autoWorkingView を使ってください。(" + ex.Message + ")");
                        }
                    }
                }
                else
                {
                    return ResultUtil.Err("ビューにView Templateが適用されています。デタッチするか autoWorkingView を使ってください。");
                }
            }

            // 対象カテゴリ
            var catsArr = p["categoryIds"] as JArray;
            var targetCatIds = (catsArr != null)
                ? catsArr.Values<int>().Select(i => Autodesk.Revit.DB.ElementIdCompat.From(i)).ToList()
                : null;

            // 対象パラメータ
            var paramSpec = p["param"] as JObject ?? new JObject();
            var paramName = paramSpec.Value<string>("name");
            var parameterId = paramSpec.Value<int?>("parameterId");
            var builtinId = paramSpec.Value<int?>("builtinId");
            if (string.IsNullOrEmpty(paramName) && !parameterId.HasValue && !builtinId.HasValue)
                return ResultUtil.Err("param.name / param.parameterId / param.builtinId のいずれかを指定してください。");

            // ユニットモード
            var unitsMode = (p.Value<string>("units") ?? "auto").ToLowerInvariant();

            // ルール
            var rules = ParseRules(p["rules"] as JArray);
            if (rules.Count == 0) return ResultUtil.Err("rules が空です。");

            // ビュー内の要素収集
            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType();

            if (targetCatIds != null && targetCatIds.Count > 0)
            {
                var catFilter = new LogicalOrFilter(targetCatIds
                    .Select(id => new ElementCategoryFilter(id)).Cast<ElementFilter>().ToList());
                collector = collector.WherePasses(catFilter);
            }

            // Solid フィルパターン（面の前景用）
            var solidFill = GetSolidFillPatternId(doc);

            // バッチ適用（長時間トランザクション回避）
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 800));
            int maxMillisPerTx = Math.Max(500, Math.Min(10000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var elems = collector.ToElements();
            int updated = 0;
            var skipped = new List<object>();
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;

            while (nextIndex < elems.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var t = new Transaction(doc, "[MCP] Apply Conditional Coloring (batched)"))
                {
                    try
                    {
                        t.Start();
                        int end = Math.Min(elems.Count, nextIndex + batchSize);
                        for (int i = nextIndex; i < end; i++)
                        {
                            var e = elems[i];
                            try
                            {
                                double? siValue = TryGetParamSiValue(e, paramName, parameterId, builtinId, unitsMode);
                                if (siValue == null)
                                {
                                    skipped.Add(new { elementId = e.Id.IntValue(), reason = "parameter not found or null/double以外" });
                                    continue;
                                }
                                var rule = SelectRule(rules, siValue.Value);
                                if (rule == null)
                                {
                                    skipped.Add(new { elementId = e.Id.IntValue(), reason = "no rule matched" });
                                    continue;
                                }
                                var ogs = new OverrideGraphicSettings();
                                var col = new Color((byte)rule.Color.r, (byte)rule.Color.g, (byte)rule.Color.b);
                                ogs.SetProjectionLineColor(col);
                                ogs.SetCutLineColor(col);
                                if (rule.Transparency is int tr)
                                { tr = Math.Max(0, Math.Min(100, tr)); ogs.SetSurfaceTransparency(tr); }
                                if (rule.ApplyFill && solidFill != ElementId.InvalidElementId)
                                {
                                    ogs.SetSurfaceForegroundPatternId(solidFill);
                                    ogs.SetSurfaceForegroundPatternColor(col);
                                    ogs.SetCutForegroundPatternId(solidFill);
                                    ogs.SetCutForegroundPatternColor(col);
                                }
                                view.SetElementOverrides(e.Id, ogs);
                                updated++;
                            }
                            catch (Exception rx)
                            {
                                skipped.Add(new { elementId = e.Id.IntValue(), reason = rx.Message });
                            }
                        }
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { t.RollBack(); } catch { }
                        skipped.Add(new { reason = "transaction failed: " + ex.Message });
                        break;
                    }
                }
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break;
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                updated,
                skippedCount = skipped.Count,
                skipped,
                completed = nextIndex >= elems.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds
            });
        }

        private class Rule
        {
            public double Lte;
            public (int r, int g, int b) Color;
            public int? Transparency;
            public bool ApplyFill;
        }

        private List<Rule> ParseRules(JArray? arr)
        {
            var list = new List<Rule>();
            if (arr == null) return list;

            foreach (var j in arr.OfType<JObject>())
            {
                var lte = j.Value<double?>("lte");
                var color = j["color"] as JObject;
                if (lte == null || color == null) continue;

                list.Add(new Rule
                {
                    Lte = lte.Value,
                    Color = (
                        color.Value<int?>("r") ?? 0,
                        color.Value<int?>("g") ?? 0,
                        color.Value<int?>("b") ?? 0
                    ),
                    Transparency = j.Value<int?>("transparency"),
                    ApplyFill = j.Value<bool?>("applyFill") == true
                });
            }

            return list.OrderBy(x => x.Lte).ToList();
        }

        private Rule? SelectRule(List<Rule> rules, double value)
        {
            foreach (var r in rules)
                if (value <= r.Lte) return r;
            return null;
        }

        private ElementId GetSolidFillPatternId(Document doc) 
        { 
            return SolidFillCache.GetOrBuild(doc); 
        } 

        private static class SolidFillCache
        {
            private static readonly object _gate = new object();
            private static readonly System.Collections.Generic.Dictionary<int, ElementId> _map = new System.Collections.Generic.Dictionary<int, ElementId>();
            public static ElementId GetOrBuild(Document doc)
            {
                if (doc == null) return ElementId.InvalidElementId;
                int key = doc.GetHashCode();
                lock (_gate)
                {
                    if (_map.TryGetValue(key, out var id) && id != null && id != ElementId.InvalidElementId)
                        return id;
                    var fps = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>();
                    foreach (var f in fps)
                    {
                        try
                        {
                            var fp = f.GetFillPattern();
                            if (fp != null && fp.IsSolidFill)
                            {
                                _map[key] = f.Id;
                                return f.Id;
                            }
                        }
                        catch { }
                    }
                    _map[key] = ElementId.InvalidElementId;
                    return ElementId.InvalidElementId;
                }
            }
        }

        // ------------------------------------------------------------
        // 2) 文字列パラメータ別（カテゴリカル）に色分け
        // ------------------------------------------------------------
        private object ApplyCategorical(UIApplication uiapp, Document doc, JObject p)
        {
            // View 解決
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            View view = null;
            if (reqViewId > 0)
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(reqViewId)) as View;
            if (view == null)
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);

            if (view == null) return ResultUtil.Err("viewId が無効、またはアクティブビュー取得に失敗しました。");

            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true;
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (detachTemplate)
                {
                    using (var tx0 = new Transaction(doc, "Detach View Template (CategoricalColoring)"))
                    { try { tx0.Start(); view.ViewTemplateId = ElementId.InvalidElementId; tx0.Commit(); } catch { try { tx0.RollBack(); } catch { } } }
                }
                else if (autoWorkingView)
                {
                    using (var tx = new Transaction(doc, "Create Working 3D (CategoricalColoring)"))
                    {
                        try
                        {
                            tx.Start();
                            var vtf = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                            if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                            var v3d = View3D.CreateIsometric(doc, vtf.Id);
                            v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                            tx.Commit();
                            view = v3d;
                            try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            return ResultUtil.Err("ビューにView Templateが適用中です。detachViewTemplate をtrueにするか、autoWorkingView をtrueにしてください。(" + ex.Message + ")");
                        }
                    }
                }
                else
                {
                    return ResultUtil.Err("ビューにView Templateが適用中です。detachViewTemplate か autoWorkingView を指定してください。");
                }
            }

            // 対象カテゴリ/クラス
            var catsArr = p["categoryIds"] as JArray;
            var targetCatIds = (catsArr != null)
                ? catsArr.Values<int>().Select(i => Autodesk.Revit.DB.ElementIdCompat.From(i)).ToList()
                : null;
            string className = (p.Value<string>("className") ?? string.Empty).Trim();

            // パラメータ指定（name / parameterId / builtinId / builtinName / guid, target: instance|type|both）
            var paramSpec = p["param"] as JObject ?? new JObject();
            var paramName = (paramSpec.Value<string>("name") ?? string.Empty).Trim();
            var parameterId = paramSpec.Value<int?>("parameterId");
            var builtinId = paramSpec.Value<int?>("builtinId");
            var builtinName = (paramSpec.Value<string>("builtInName") ?? string.Empty).Trim();
            var guidStr = (paramSpec.Value<string>("guid") ?? string.Empty).Trim();
            string target = (paramSpec.Value<string>("target") ?? "both").Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(paramName) && !parameterId.HasValue && !builtinId.HasValue && string.IsNullOrEmpty(builtinName) && string.IsNullOrEmpty(guidStr))
                return ResultUtil.Err("param: name / parameterId / builtinId / builtInName / guid のいずれかを指定してください。");

            // パレット
            string paletteName = (p.Value<string>("palette") ?? "Set3").Trim();
            var colors = GetCategoricalPalette(paletteName);
            var solidFill = GetSolidFillPatternId(doc);

            // バッチ処理
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 800));
            int maxMillisPerTx = Math.Max(500, Math.Min(10000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var collector = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType();
            if (targetCatIds != null && targetCatIds.Count > 0)
            {
                var catFilter = new LogicalOrFilter(targetCatIds
                    .Select(id => new ElementCategoryFilter(id)).Cast<ElementFilter>().ToList());
                collector = collector.WherePasses(catFilter);
            }

            var elems = collector.ToElements();
            // まず全要素から distinct 値を収集（フィルタ考慮）
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in elems)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(className))
                    {
                        if (!string.Equals(e.GetType()?.Name ?? string.Empty, className, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    var val = ResolveParamStringForTargets(doc, e, target, paramName, builtinName, builtinId, guidStr);
                    if (!string.IsNullOrWhiteSpace(val) && seen.Add(val)) distinct.Add(val);
                }
                catch { }
            }

            // 値→色 マップ
            var valueToColor = new Dictionary<string, (int r, int g, int b)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < distinct.Count; i++)
            {
                var c = colors[i % colors.Count];
                valueToColor[distinct[i]] = c;
            }

            int updated = 0;
            var skipped = new List<object>();
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;

            while (nextIndex < elems.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var t = new Transaction(doc, "[MCP] Apply Categorical Coloring (batched)"))
                {
                    try
                    {
                        t.Start();
                        int end = Math.Min(elems.Count, nextIndex + batchSize);
                        for (int i = nextIndex; i < end; i++)
                        {
                            var e = elems[i];
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(className))
                                {
                                    if (!string.Equals(e.GetType()?.Name ?? string.Empty, className, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }
                                var val = ResolveParamStringForTargets(doc, e, target, paramName, builtinName, builtinId, guidStr);
                                if (string.IsNullOrWhiteSpace(val))
                                {
                                    skipped.Add(new { elementId = e.Id.IntValue(), reason = "param empty" });
                                    continue;
                                }
                                if (!valueToColor.TryGetValue(val, out var cTriplet))
                                {
                                    // 予期せぬ（並行で値が増えた等）→スキップ
                                    skipped.Add(new { elementId = e.Id.IntValue(), reason = "value not mapped" });
                                    continue;
                                }
                                var ogs = new OverrideGraphicSettings();
                                var col = new Color((byte)cTriplet.r, (byte)cTriplet.g, (byte)cTriplet.b);
                                ogs.SetProjectionLineColor(col);
                                ogs.SetCutLineColor(col);
                                if (solidFill != ElementId.InvalidElementId)
                                {
                                    ogs.SetSurfaceForegroundPatternId(solidFill);
                                    ogs.SetSurfaceForegroundPatternColor(col);
                                    ogs.SetCutForegroundPatternId(solidFill);
                                    ogs.SetCutForegroundPatternColor(col);
                                }
                                view.SetElementOverrides(e.Id, ogs);
                                updated++;
                            }
                            catch (Exception rx)
                            {
                                skipped.Add(new { elementId = e.Id.IntValue(), reason = rx.Message });
                            }
                        }
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { t.RollBack(); } catch { }
                        skipped.Add(new { reason = "transaction failed: " + ex.Message });
                        break;
                    }
                }
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break;
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                updated,
                skippedCount = skipped.Count,
                skipped,
                categories = targetCatIds?.Select(x => x.IntValue()).ToList(),
                distinctValues = distinct,
                palette = paletteName,
                completed = nextIndex >= elems.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds
            });
        }

        private string ResolveParamStringForTargets(Document doc, Element e, string target, string name, string builtInName, int? builtinId, string guidStr)
        {
            string v = string.Empty;
            if (target == "instance" || target == "both")
                v = ResolveParamStringOn(e, name, builtInName, builtinId, guidStr);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            if (target == "type" || target == "both")
            {
                try
                {
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    if (et != null)
                    {
                        v = ResolveParamStringOn(et, name, builtInName, builtinId, guidStr);
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        private string ResolveParamStringOn(Element e, string name, string builtInName, int? builtinId, string guidStr)
        {
            try
            {
                Parameter p = null;
                if (builtinId.HasValue) { try { p = e.get_Parameter((BuiltInParameter)builtinId.Value); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(builtInName)) { try { p = e.get_Parameter((BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtInName, true)); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(guidStr)) { try { p = e.get_Parameter(new Guid(guidStr)); } catch { } }
                if (p == null && !string.IsNullOrWhiteSpace(name)) { try { p = e.LookupParameter(name); } catch { } }
                if (p == null) return string.Empty;
                if (p.StorageType == StorageType.String) return p.AsString() ?? string.Empty;
                return p.AsValueString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private List<(int r, int g, int b)> GetCategoricalPalette(string name)
        {
            // 少数カテゴリ向けセット（ColorBrewer Set3 風 + 補助）
            var set3 = new List<(int r, int g, int b)>
            {
                (141,211,199), (255,255,179), (190,186,218), (251,128,114), (128,177,211),
                (253,180,98), (179,222,105), (252,205,229), (217,217,217), (188,128,189),
                (204,235,197), (255,237,111)
            };
            var paired = new List<(int r, int g, int b)>
            {
                (166,206,227),(31,120,180),(178,223,138),(51,160,44),(251,154,153),(227,26,28),
                (253,191,111),(255,127,0),(202,178,214),(106,61,154),(255,255,153),(177,89,40)
            };
            var set1 = new List<(int r, int g, int b)>
            {
                (228,26,28),(55,126,184),(77,175,74),(152,78,163),(255,127,0),(255,255,51),
                (166,86,40),(247,129,191),(153,153,153)
            };

            var nameLc = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (nameLc.Contains("paired")) return paired;
            if (nameLc.Contains("set1")) return set1;
            // default
            return set3;
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {i++}";
            }
            return name;
        }

        /// <summary>
        /// 対象パラメータ値を SI に正規化して返す（unitsMode: auto/mm/m2/m3/deg/raw）
        /// </summary>
        private double? TryGetParamSiValue(Element e, string? name, int? parameterId, int? builtinId, string unitsMode)
        {
            Parameter? prm = null;

            if (parameterId.HasValue)
            {
                prm = e.Parameters.Cast<Parameter>()
                    .FirstOrDefault(x => x.Id?.IntValue() == parameterId.Value);
            }
            else if (builtinId.HasValue)
            {
                prm = e.get_Parameter((BuiltInParameter)builtinId.Value);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                prm = e.LookupParameter(name);
            }

            if (prm == null || prm.StorageType != StorageType.Double)
                return null;

            var spec = UnitHelper.GetSpec(prm);   // 既存または本回答の UnitHelper 追記を利用
            var raw = prm.AsDouble();             // 内部値（ft/rad等）

            switch (unitsMode)
            {
                case "raw":
                    return raw;
                case "mm":
                    return UnitHelper.ConvertFromInternalBySpec(raw, spec, "mm");
                case "m2":
                    return UnitHelper.ConvertFromInternalBySpec(raw, spec, "m2");
                case "m3":
                    return UnitHelper.ConvertFromInternalBySpec(raw, spec, "m3");
                case "deg":
                    return UnitHelper.ConvertFromInternalBySpec(raw, spec, "deg");
                case "auto":
                default:
                    return UnitHelper.ConvertFromInternalBySpec(raw, spec, "auto");
            }
        }

        // ------------------------------------------------------------
        // 2) Color Fill への委譲（既存の apply_quick_color_scheme を利用）
        // ------------------------------------------------------------
        private object ForwardToColorFill(UIApplication uiapp, Document doc, JObject p)
        {
            var viewId = p.Value<int?>("viewId")
                ?? uiapp.ActiveUIDocument.ActiveView?.Id.IntValue()
                ?? 0;

            var payload = new JObject
            {
                ["ok"] = true,
                ["forward"] = new JObject
                {
                    ["method"] = "apply_quick_color_scheme",
                    ["params"] = new JObject
                    {
                        ["viewId"] = viewId,
                        ["categoryId"] = p.Value<int?>("categoryId"),
                        ["areaSchemeId"] = p["areaSchemeId"], // null可（Areasなら推奨は明示指定）
                        ["paramDefinitionId"] = p.Value<int?>("paramDefinitionId"),
                        ["mode"] = p.Value<string>("modeKind") ?? "sequential",
                        ["palette"] = p.Value<string>("palette") ?? "YlOrRd",
                        ["bins"] = p.Value<int?>("bins") ?? 7,
                        ["createLegend"] = p.Value<bool?>("createLegend") ?? true,
                        ["legendOrigin"] = p["legendOrigin"] ?? new JObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 }
                    }
                },
                ["notes"] = "この応答の 'forward' に従って apply_quick_color_scheme を呼び出してください（Rooms/Areas/Spaces の凡例付きヒートマップ）。"
            };

            return payload;
        }
    }

    public class ClearConditionalColoringCommand : IRevitCommandHandler
    {
        public string CommandName => "clear_conditional_coloring";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject?)cmd.Params ?? new JObject();

            View view;
            var viewId = p.Value<int?>("viewId");
            if (viewId.HasValue)
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            else
                view = uiapp.ActiveUIDocument.ActiveView;

            if (view == null) return ResultUtil.Err("viewId が不正、またはアクティブビューを取得できません。");

            var alsoReset = p.Value<bool?>("alsoResetAllViewOverrides") == true;

            using (var t = new Transaction(doc, "[MCP] Clear Conditional Coloring"))
            {
                t.Start();

                if (alsoReset)
                {
                    // 既存の reset_all_view_overrides を呼ぶ方が堅牢（ここは簡易版）
                    RemoveAllElementOverrides(view);
                }
                else
                {
                    // 要素オーバーライドのみ解除（ビュー内要素対象）
                    var ids = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds();

                    foreach (var id in ids)
                        view.SetElementOverrides(id, new OverrideGraphicSettings());
                }

                t.Commit();
            }

            // Color Fill の解除は既存コマンド remove_color_scheme_from_view を推奨。
            return ResultUtil.Ok(new { ok = true, viewId = view.Id.IntValue() });
        }

        private void RemoveAllElementOverrides(View v)
        {
            var doc = v.Document;
            var ids = new FilteredElementCollector(doc, v.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
                v.SetElementOverrides(id, new OverrideGraphicSettings());
        }
    }
}


