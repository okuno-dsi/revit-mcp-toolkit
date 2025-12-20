// ================================================================
// File: Commands/Export/ExportDwgCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 2Dビューに「表示されているそのまま」をDWGへ書き出す
// Notes  :
//   - ファイル名: 必ずサニタイズ → .dwg 強制付与（既存ファイルとは衝突回避）
//   - 失敗時: export_<viewId>_<n>.dwg で自動フォールバック再試行
//   - ビュー名: 複製ビューに安全名を付与（Revitの禁止文字で落ちないように）
//   - Enum.TryParse<BuiltInCategory> 使用（.NET4.8対応）
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitMCPAddin.Commands.Export
{
    public class ExportDwgCommand : IRevitCommandHandler
    {
        public string CommandName => "export_dwg";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            try
            {
                // Batch mode: items[] + time-slice controls
                var itemsArr = p["items"] as JArray;
                if (itemsArr != null && itemsArr.Count > 0)
                {
                    int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                    int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 10);
                    int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 0);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var results = new List<object>();
                    int processed = 0; int nextIndex = startIndex;

                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        // Inherit top-level defaults when missing on item
                        var merged = new JObject(p);
                        foreach (var prop in it.Properties()) merged[prop.Name] = prop.Value;
                        var res = ExportSingle(uiapp, merged);
                        results.Add(res);
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }

                    bool completed = nextIndex >= itemsArr.Count;
                    return new { ok = true, processed, completed, nextIndex = completed ? (int?)null : nextIndex, results };
                }

                // -------- 1) 対象ビューの特定 --------
                View baseView = null;
                int? viewIdIn = p.Value<int?>("viewId");
                string viewUidIn = p.Value<string>("viewUniqueId");

                if (viewIdIn.HasValue && viewIdIn.Value > 0)
                {
                    var v = doc.GetElement(new ElementId(viewIdIn.Value)) as View;
                    if (v == null) return new { ok = false, msg = $"viewId={viewIdIn} のビューが見つかりません。" };
                    baseView = v;
                }
                else if (!string.IsNullOrWhiteSpace(viewUidIn))
                {
                    var v = doc.GetElement(viewUidIn) as View;
                    if (v == null) return new { ok = false, msg = $"viewUniqueId={viewUidIn} のビューが見つかりません。" };
                    baseView = v;
                }
                else
                {
                    baseView = uidoc.ActiveView;
                    if (baseView == null) return new { ok = false, msg = "アクティブビューが特定できません。" };
                }

                // -------- 2) パラメータ取得 --------
                var outputFolder = p.Value<string>("outputFolder");
                if (string.IsNullOrWhiteSpace(outputFolder)) return new { ok = false, msg = "outputFolder は必須です。" };
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

                var fileNameIn = p.Value<string>("fileName");           // 拡張子付き/無しどちらでも可
                var categoriesIn = p["categories"]?.Values<string>()?.ToArray() ?? Array.Empty<string>();
                var elemIdsIn = p["elementIds"]?.Values<int>()?.Select(i => new ElementId(i)).ToList() ?? new List<ElementId>();
                var dwgVerIn = p.Value<string>("dwgVersion");         // 例: "ACAD2018"
                var setupNameIn = p.Value<string>("useExportSetup");     // Revit側の「DWG出力設定」名
                var keepTempView = p.Value<bool?>("keepTempView") ?? false;

                // -------- 3) 複製ビュー作成（元ビューは触らない） --------
                View workingView = null;
                ElementId dupId = ElementId.InvalidElementId;
                using (var t = new Transaction(doc, "Duplicate View for DWG Export"))
                {
                    t.Start();
                    dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                    workingView = (View)doc.GetElement(dupId);

                    // ★ ビュー名もサニタイズして安全な名前にする（Revit の名前検証で落ちないように）
                    var safeBaseViewName = SanitizeElementName(baseView.Name);
                    workingView.Name = $"{safeBaseViewName} DWG {DateTime.Now:HHmmss}";

                    t.Commit();
                }

                int hiddenCount = 0;
                int exportedElemCount = 0;

                // -------- 4) Isolateロジック（カテゴリ/要素） --------
                if (categoriesIn.Length > 0 || elemIdsIn.Count > 0)
                {
                    // Detach template on the duplicated view to prevent template rules hiding everything
                    try
                    {
                        if (workingView.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            using (var txDt = new Transaction(doc, "Detach Template (DWG Export)"))
                            {
                                txDt.Start();
                                workingView.ViewTemplateId = ElementId.InvalidElementId;
                                txDt.Commit();
                            }
                        }
                    }
                    catch { /* best effort */ }

                    // Ensure categories of target elements are visible in the working view (best-effort)
                    try
                    {
                        var targetElemIds = new HashSet<ElementId>(elemIdsIn ?? new List<ElementId>());
                        if (targetElemIds.Count == 0 && categoriesIn.Length > 0)
                        {
                            // fallback: collect visible elems first then filter by categories
                            var prelim = new FilteredElementCollector(doc, workingView.Id)
                                .WhereElementIsNotElementType()
                                .ToElements();
                            foreach (var e in prelim)
                            {
                                var cat = e.Category?.Name ?? string.Empty;
                                if (!string.IsNullOrEmpty(cat) && categoriesIn.Contains(cat, StringComparer.OrdinalIgnoreCase))
                                    targetElemIds.Add(e.Id);
                            }
                        }

                        if (targetElemIds.Count > 0)
                        {
                            var targetCats = new HashSet<ElementId>();
                            foreach (var id in targetElemIds)
                            {
                                try
                                {
                                    var el = doc.GetElement(id);
                                    var cat = el?.Category;
                                    if (cat != null) targetCats.Add(cat.Id);
                                }
                                catch { }
                            }

                            using (var txCat = new Transaction(doc, "Ensure Target Categories Visible (DWG Export)"))
                            {
                                try
                                {
                                    txCat.Start();
                                    foreach (var cid in targetCats)
                                    {
                                        try
                                        {
                                            bool canHide = false; try { canHide = workingView.CanCategoryBeHidden(cid); } catch { }
                                            if (canHide)
                                                workingView.SetCategoryHidden(cid, false);
                                        }
                                        catch { /* ignore per-category */ }
                                    }
                                    txCat.Commit();
                                }
                                catch { try { txCat.RollBack(); } catch { } }
                            }
                        }
                    }
                    catch { /* best effort */ }

                    var visibleElems = new FilteredElementCollector(doc, workingView.Id)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    var targetSet = new HashSet<ElementId>();

                    // a) カテゴリ指定 → BuiltInCategory名 or 表示名で解決
                    if (categoriesIn.Length > 0)
                    {
                        var catIdsToKeep = ResolveCategories(doc, categoriesIn);
                        foreach (var e in visibleElems)
                        {
                            try
                            {
                                var cat = e.Category;
                                if (cat != null && catIdsToKeep.Contains(cat.Id))
                                    targetSet.Add(e.Id);
                            }
                            catch { /* ignore */ }
                        }
                    }

                    // b) 要素指定（ビューに見えるものだけ）
                    if (elemIdsIn.Count > 0)
                    {
                        var visibleIdSet = new HashSet<ElementId>(visibleElems.Select(x => x.Id));
                        foreach (var id in elemIdsIn)
                        {
                            if (visibleIdSet.Contains(id)) targetSet.Add(id);
                        }
                    }

                    // c) Isolate相当（対象以外をHide）
                    if (targetSet.Count > 0)
                    {
                        // Hide only model elements that are not targets; keep annotations (tags/grids/dimensions) visible
                        var toHide = new List<ElementId>();
                        foreach (var e in visibleElems)
                        {
                            try
                            {
                                // Skip element types and annotation categories
                                var cat = e.Category;
                                if (cat != null && cat.CategoryType != CategoryType.Model) continue;
                                if (!targetSet.Contains(e.Id)) toHide.Add(e.Id);
                            }
                            catch { /* best effort */ }
                        }
                        if (toHide.Count > 0)
                        {
                            using (var t = new Transaction(doc, "Hide non-targets for DWG Export"))
                            {
                                t.Start();
                                workingView.HideElements(toHide);
                                hiddenCount = toHide.Count;
                                t.Commit();
                            }
                        }
                        exportedElemCount = targetSet.Count;
                    }
                    else
                    {
                        CleanupTempView(doc, workingView, keepTempView);
                        return new { ok = false, msg = "指定の要素/カテゴリで、対象ビューに可視の要素が見つかりませんでした。" };
                    }
                }
                else
                {
                    // 無指定→ビューに見えている全要素を出力対象（統計のみ）
                    exportedElemCount = new FilteredElementCollector(doc, workingView.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .Count;
                }

                // -------- 5) DWG Export オプション --------
                var opt = new DWGExportOptions();

                if (!string.IsNullOrWhiteSpace(setupNameIn))
                    TryApplyDwgExportSetupByName(doc, setupNameIn, opt);

                if (!string.IsNullOrWhiteSpace(dwgVerIn) && TryParseAcadVersion(dwgVerIn, out var ver))
                    opt.FileVersion = ver;

                // -------- 6) 出力ファイル名の確定（必ずサニタイズ → .dwg 付与） --------
                var baseName = NormalizeBaseName(fileNameIn, workingView);
                var outPath = MakeDwgPath(outputFolder, baseName);

                // -------- 7) エクスポート実行（失敗時はフォールバック名で再試行） --------
                bool exportOk = TryExport(doc, workingView.Id, opt, outPath, outputFolder, out var primaryError);

                if (!exportOk)
                {
                    // フォールバック：export_<viewId>_<n>.dwg
                    var fallbackPath = MakeFallbackDwgPath(outputFolder, baseView.Id.IntegerValue, 1);
                    bool retryOk = TryExport(doc, workingView.Id, opt, fallbackPath, outputFolder, out var fallbackError);

                    CleanupTempView(doc, workingView, keepTempView);

                    if (!retryOk)
                    {
                        var reason = JoinNonEmpty(new[]
                        {
                            primaryError != null ? $"primary: {primaryError.Message}" : null,
                            fallbackError != null ? $"fallback: {fallbackError.Message}" : null
                        }, " / ");
                        return new { ok = false, msg = "DWG export failed. " + reason };
                    }

                    return new
                    {
                        ok = true,
                        path = fallbackPath,
                        viewId = baseView.Id.IntegerValue,
                        workingViewId = keepTempView && workingView != null ? (int?)workingView.Id.IntegerValue : null,
                        workingViewName = keepTempView && workingView != null ? workingView.Name : null,
                        exportedElementCount = exportedElemCount,
                        hiddenCount,
                        notes = "Primary export failed; fallback name used."
                    };
                }

                CleanupTempView(doc, workingView, keepTempView);

                return new
                {
                    ok = true,
                    path = outPath,
                    viewId = baseView.Id.IntegerValue,
                    workingViewId = keepTempView && workingView != null ? (int?)workingView.Id.IntegerValue : null,
                    workingViewName = keepTempView && workingView != null ? workingView.Name : null,
                    exportedElementCount = exportedElemCount,
                    hiddenCount,
                    notes = "Exported with sanitized file name and .dwg extension."
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        // --- Export 実行の安全ラッパ -----------------------------------------
        private static bool TryExport(Document doc, ElementId viewId, DWGExportOptions opt, string desiredPath, string fallbackFolder, out Exception error)
        {
            error = null;
            try
            {
                string exportFolder = Path.GetDirectoryName(desiredPath) ?? fallbackFolder;
                string exportNameNoEx = Path.GetFileNameWithoutExtension(desiredPath); // 既にサニタイズ済みを想定
                return doc.Export(exportFolder, exportNameNoEx, new List<ElementId> { viewId }, opt);
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        // --- ファイル名：拡張子吸収→サニタイズ→空ならexport -------------------
        private static string NormalizeBaseName(string fileNameIn, View workingView)
        {
            string rawBase = !string.IsNullOrWhiteSpace(fileNameIn)
                ? Path.GetFileNameWithoutExtension(fileNameIn)     // .dwg/.DXF 等が付いていても外す
                : (workingView?.Name ?? "export");

            string safe = SanitizeFileName(rawBase);
            if (string.IsNullOrWhiteSpace(safe)) safe = "export";
            return safe;
        }

        // --- .dwg を強制付与して衝突回避 --------------------------------------
        private static string MakeDwgPath(string folder, string baseName)
        {
            return GetNonConflictingPath(folder, baseName, ".dwg");
        }

        // --- 失敗時フォールバック: export_<viewId>_<n>.dwg --------------------
        private static string MakeFallbackDwgPath(string folder, int viewId, int startFrom = 1, int max = 9999)
        {
            for (int i = startFrom; i <= max; i++)
            {
                string baseName = $"export_{viewId}_{i}";
                string path = Path.Combine(folder, baseName + ".dwg");
                if (!File.Exists(path)) return path;
            }
            return Path.Combine(folder, $"export_{viewId}_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");
        }

        // --- 一時ビューのクリーンアップ ---------------------------------------
        private static void CleanupTempView(Document doc, View v, bool keep)
        {
            if (v == null || keep) return;
            using (var t = new Transaction(doc, "Cleanup DWG Temp View"))
            {
                t.Start();
                try { doc.Delete(v.Id); } catch { /* ignore */ }
                t.Commit();
            }
        }

        // --- Revit要素名のサニタイズ（ビュー名など） ---------------------------
        private static string SanitizeElementName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Temp";
            // Revit の一般的な禁止文字と制御文字を除去
            var forbidden = new HashSet<char>(new[] { '\\', '/', ';', ':', '*', '?', '"', '<', '>', '|', '[', ']', '{', '}', '=' });
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsControl(ch) || forbidden.Contains(ch)) continue;
                sb.Append(ch);
            }
            var cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) cleaned = "Temp";
            if (cleaned.Length > 255) cleaned = cleaned.Substring(0, 255);
            return cleaned;
        }

        // --- 禁止文字/予約語など“強い”サニタイズ（ファイル名用） --------------
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "export";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(invalid.Contains(ch) ? '_' : ch);

            var cleaned = sb.ToString().Trim();
            if (cleaned.EndsWith(".")) cleaned = cleaned.TrimEnd('.');

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "CON","PRN","AUX","NUL",
                "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
            };
            if (reserved.Contains(cleaned))
                cleaned = "_" + cleaned;

            if (string.IsNullOrEmpty(cleaned)) cleaned = "export";
            return cleaned;
        }

        // --- ベース名＋拡張子で存在しないファイルパスを返す -------------------
        private static string GetNonConflictingPath(string folder, string baseName, string ext)
        {
            string safeBase = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "export";

            var path = Path.Combine(folder, safeBase + ext);
            int i = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"{safeBase} ({i}){ext}");
                i++;
            }
            return path;
        }

        // --- カテゴリ名解決（BuiltInCategory名 or 表示名） ----------------------
        private static HashSet<ElementId> ResolveCategories(Document doc, IEnumerable<string> names)
        {
            var set = new HashSet<ElementId>();
            var allCats = doc.Settings.Categories;

            foreach (var n in names.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                // 1) BuiltInCategory名での解決（.NET4.8対応のTryParse<T>）
                BuiltInCategory parsed;
                if (Enum.TryParse<BuiltInCategory>(n, true, out parsed))
                {
                    var cat = Category.GetCategory(doc, parsed);
                    if (cat != null)
                    {
                        set.Add(cat.Id);
                        continue;
                    }
                }

                // 2) 表示名一致（日本語/英語UI名）
                foreach (Category c in allCats)
                {
                    if (string.Equals(c?.Name, n, StringComparison.CurrentCultureIgnoreCase))
                    {
                        set.Add(c.Id);
                        break;
                    }
                }
            }
            return set;
        }

        // --- ACADVersion 文字列 → 列挙体 ---------------------------------------
        private static bool TryParseAcadVersion(string s, out ACADVersion v)
        {
            try
            {
                if (Enum.TryParse(s, true, out v)) return true; // "ACAD2018" 等
            }
            catch { }
            v = default;
            return false;
        }

        // --- 文字列連結のユーティリティ ----------------------------------------
        private static string JoinNonEmpty(IEnumerable<string> parts, string sep)
        {
            var list = parts?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>();
            return string.Join(sep, list);
        }

        // --- 既存のDWG出力設定（名前指定）を適用（反射で安全適用） -------------
        private static void TryApplyDwgExportSetupByName(Document doc, string setupName, DWGExportOptions opt)
        {
            try
            {
                var id = ExportSetupCache.Resolve(doc, setupName);
                if (id != ElementId.InvalidElementId)
                {
                    var mi = typeof(DWGExportOptions).GetMethod("SetExportDWGSettings");
                    if (mi != null) mi.Invoke(opt, new object[] { doc, id });
                }
            }
            catch
            {
                // 既定オプションで続行
            }
        }

        // Single export unit (no batch)
        private object ExportSingle(UIApplication uiapp, JObject p)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            try
            {
                View baseView = null;
                int? viewIdIn = p.Value<int?>("viewId");
                string viewUidIn = p.Value<string>("viewUniqueId");
                if (viewIdIn.HasValue && viewIdIn.Value > 0)
                {
                    baseView = doc.GetElement(new ElementId(viewIdIn.Value)) as View;
                    if (baseView == null) return new { ok = false, msg = $"viewId={viewIdIn} のビューが見つかりません。" };
                }
                else if (!string.IsNullOrWhiteSpace(viewUidIn))
                {
                    baseView = doc.GetElement(viewUidIn) as View;
                    if (baseView == null) return new { ok = false, msg = $"viewUniqueId={viewUidIn} のビューが見つかりません。" };
                }
                else
                {
                    baseView = uidoc.ActiveView;
                    if (baseView == null) return new { ok = false, msg = "アクティブビューが特定できません。" };
                }

                var outputFolder = p.Value<string>("outputFolder");
                if (string.IsNullOrWhiteSpace(outputFolder)) return new { ok = false, msg = "outputFolder は必須です。" };
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

                var fileNameIn = p.Value<string>("fileName");
                var categoriesIn = p["categories"]?.Values<string>()?.ToArray() ?? Array.Empty<string>();
                var elemIdsIn = p["elementIds"]?.Values<int>()?.Select(i => new ElementId(i)).ToList() ?? new List<ElementId>();
                var dwgVerIn = p.Value<string>("dwgVersion");
                var setupNameIn = p.Value<string>("useExportSetup");
                var keepTempView = p.Value<bool?>("keepTempView") ?? false;

                // Duplicate view
                View workingView = null; ElementId dupId = ElementId.InvalidElementId;
                using (var t = new Transaction(doc, "Duplicate View for DWG Export"))
                {
                    t.Start();
                    dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                    workingView = (View)doc.GetElement(dupId);
                    var safeBaseViewName = SanitizeElementName(baseView.Name);
                    workingView.Name = $"{safeBaseViewName} DWG {DateTime.Now:HHmmss}";
                    t.Commit();
                }

                int hiddenCount = 0; int exportedElemCount = 0;
                if (categoriesIn.Length > 0 || elemIdsIn.Count > 0)
                {
                    try
                    {
                        if (workingView.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            using (var txDt = new Transaction(doc, "Detach Template (DWG Export)"))
                            {
                                try { txDt.Start(); workingView.ViewTemplateId = ElementId.InvalidElementId; txDt.Commit(); }
                                catch { try { txDt.RollBack(); } catch { } }
                            }
                        }
                        var targetCats = new HashSet<ElementId>();
                        if (categoriesIn.Length > 0)
                        {
                            foreach (var n in categoriesIn)
                            {
                                try
                                {
                                    BuiltInCategory bic;
                                    if (Enum.TryParse<BuiltInCategory>(n, true, out bic))
                                    {
                                        var cat = Category.GetCategory(doc, bic);
                                        if (cat != null) targetCats.Add(cat.Id);
                                    }
                                    else
                                    {
                                        foreach (Category c in doc.Settings.Categories)
                                            if (string.Equals(c?.Name, n, StringComparison.CurrentCultureIgnoreCase)) { targetCats.Add(c.Id); break; }
                                    }
                                }
                                catch { }
                            }
                            using (var txCat = new Transaction(doc, "Ensure Target Categories Visible (DWG Export)"))
                            {
                                try
                                {
                                    txCat.Start();
                                    foreach (var cid in targetCats)
                                    {
                                        try { bool canHide = false; try { canHide = workingView.CanCategoryBeHidden(cid); } catch { } if (canHide) workingView.SetCategoryHidden(cid, false); } catch { }
                                    }
                                    txCat.Commit();
                                }
                                catch { try { txCat.RollBack(); } catch { } }
                            }
                        }
                    }
                    catch { }

                    var visibleElems = new FilteredElementCollector(doc, workingView.Id).WhereElementIsNotElementType().ToElements();
                    var targetSet = new HashSet<ElementId>();
                    if (categoriesIn.Length > 0)
                    {
                        var catIdsToKeep = ResolveCategories(doc, categoriesIn);
                        foreach (var e in visibleElems)
                        {
                            try { var cat = e.Category; if (cat != null && catIdsToKeep.Contains(cat.Id)) targetSet.Add(e.Id); } catch { }
                        }
                    }
                    if (elemIdsIn.Count > 0)
                    {
                        var visibleIdSet = new HashSet<ElementId>(visibleElems.Select(x => x.Id));
                        foreach (var id in elemIdsIn) if (visibleIdSet.Contains(id)) targetSet.Add(id);
                    }
                    if (targetSet.Count > 0)
                    {
                        var toHide = new List<ElementId>();
                        foreach (var e in visibleElems)
                        {
                            try { var cat = e.Category; if (cat != null && cat.CategoryType != CategoryType.Model) continue; if (!targetSet.Contains(e.Id)) toHide.Add(e.Id); } catch { }
                        }
                        if (toHide.Count > 0)
                        {
                            using (var t = new Transaction(doc, "Hide non-targets for DWG Export"))
                            { t.Start(); workingView.HideElements(toHide); hiddenCount = toHide.Count; t.Commit(); }
                        }
                        exportedElemCount = targetSet.Count;
                    }
                    else
                    {
                        CleanupTempView(doc, workingView, keepTempView);
                        return new { ok = false, msg = "指定の要素/カテゴリで、対象ビューに可視の要素が見つかりませんでした。" };
                    }
                }
                else
                {
                    exportedElemCount = new FilteredElementCollector(doc, workingView.Id).WhereElementIsNotElementType().ToElementIds().Count;
                }

                // Options and export
                var opt = new DWGExportOptions();
                if (!string.IsNullOrWhiteSpace(dwgVerIn) && TryParseAcadVersion(dwgVerIn, out var ver)) opt.FileVersion = ver;
                if (!string.IsNullOrWhiteSpace(setupNameIn)) TryApplyDwgExportSetupByName(doc, setupNameIn, opt);
                var baseName = string.IsNullOrWhiteSpace(fileNameIn) ? SanitizeElementName(baseView.Name) : fileNameIn;
                var outPath = GetNonConflictingPath(outputFolder, baseName, ".dwg");
                var viewIds = new List<ElementId> { workingView.Id };
                doc.Export(Path.GetDirectoryName(outPath), Path.GetFileName(outPath), viewIds, opt);

                CleanupTempView(doc, workingView, keepTempView);

                return new { ok = true, path = outPath, hiddenCount, exportedElemCount };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static class ExportSetupCache
        {
            private static readonly object _gate = new object();
            private static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, ElementId>> _map
                = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, ElementId>>();

            public static ElementId Resolve(Document doc, string setupName)
            {
                if (doc == null || string.IsNullOrWhiteSpace(setupName)) return ElementId.InvalidElementId;
                int key = doc.GetHashCode();
                lock (_gate)
                {
                    if (_map.TryGetValue(key, out var dict))
                    {
                        if (dict.TryGetValue(setupName, out var cached)) return cached;
                    }
                    else
                    {
                        dict = new System.Collections.Generic.Dictionary<string, ElementId>(System.StringComparer.OrdinalIgnoreCase);
                        _map[key] = dict;
                    }

                    // scan once and cache
                    try
                    {
                        var collector = new FilteredElementCollector(doc).OfClass(typeof(Element));
                        var candidates = collector.Where(e =>
                        {
                            var cn = e.GetType().Name;
                            return cn.Contains("ExportDWGSettings") || cn.Contains("DWGExportSettings");
                        }).ToList();
                        foreach (var e in candidates)
                        {
                            string n = null;
                            try
                            {
                                var nameParam = e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
                                                ?? e.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)
                                                ?? e.LookupParameter("Name");
                                n = nameParam?.AsString();
                            }
                            catch { }
                            if (!string.IsNullOrEmpty(n) && !dict.ContainsKey(n)) dict[n] = e.Id;
                        }
                        if (dict.TryGetValue(setupName, out var found)) return found;
                    }
                    catch { }
                    return ElementId.InvalidElementId;
                }
            }
        }
    }
}
