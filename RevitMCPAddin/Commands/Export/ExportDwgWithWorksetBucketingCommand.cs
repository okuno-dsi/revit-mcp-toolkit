// ======================================================================
// File: Commands/Export/ExportDwgWithWorksetBucketingCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 「一時ワークセット振替 → 純正DWG出力 → 即ロールバック」
//          で “DWGの画は純正のまま” かつ “Workset レイヤ修飾子で細分化” を実現
// Notes  :
//   - ルールで可視要素をワークセットに一時振替（Element毎）
//   - Export 後に TransactionGroup.RollBack() で完全に元へ戻す
//   - 既存の ExportDwgCommand と並存（内部ロジックは最小限重複）
//   - DWG 側は「DWG/DXFのエクスポート設定」で Layer modifiers: Workset を有効にしておくこと
//   - View は複製して操作（元ビューは触らない）
//   - ルールは “先勝ち、AND条件” の簡易実装
// ======================================================================
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
using System.Text.RegularExpressions;

namespace RevitMCPAddin.Commands.Export
{
    public class ExportDwgWithWorksetBucketingCommand : IRevitCommandHandler
    {
        public string CommandName => "export_dwg_with_workset_bucketing";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            if (!doc.IsWorkshared) return new { ok = false, msg = "ワークシェアリングが有効化されていません（Worksets）。" };

            var p = (JObject)cmd.Params;

            try
            {
                // -------- 1) 対象ビューの特定（元ビューは触らない） --------
                View baseView = ResolveTargetView(doc, uidoc, p);
                if (baseView == null) return new { ok = false, msg = "対象ビューが特定できません。" };
                if (!baseView.CanBePrinted) return new { ok = false, msg = "対象ビューはエクスポート不可です。" };

                // -------- 2) 出力先/オプション --------
                var outputFolder = p.Value<string>("outputFolder");
                if (string.IsNullOrWhiteSpace(outputFolder)) return new { ok = false, msg = "outputFolder は必須です。" };
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

                var fileNameIn = p.Value<string>("fileName");                // base name（拡張子は外す）
                var setupNameIn = p.Value<string>("useExportSetup");         // 既存「DWG 出力設定」名（任意）
                var dwgVerIn = p.Value<string>("dwgVersion");             // 例: "ACAD2018"（任意）
                var keepTempView = p.Value<bool?>("keepTempView") ?? false;  // デバッグ時のみ true 推奨

                // ルール & 既定ワークセット名
                var rules = ParseRules(p["rules"] as JArray);
                PrepareRules(rules); // precompile regex to reduce per-element cost
                string unmatched = p.Value<string>("unmatchedWorksetName") ?? "WS_UNMATCHED";

                // -------- 3) 作業ビュー複製（安全運転） --------
                View workingView;
                using (var t = new Transaction(doc, "Duplicate View for DWG Export (Workset Bucketing)"))
                {
                    t.Start();
                    var dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                    workingView = (View)doc.GetElement(dupId);

                    var safeBaseViewName = SanitizeElementName(baseView.Name);
                    workingView.Name = $"{safeBaseViewName} Bucketing {DateTime.Now:HHmmss}";
                    t.Commit();
                }

                // -------- 4) ビューに「見えている要素」だけ対象 --------
                var visibleElems = new FilteredElementCollector(doc, workingView.Id)
                                        .WhereElementIsNotElementType()
                                        .ToElements();

                // ビュー固有（注記/寸法/詳細線 等）は除外：Workset を持たない or 変更不可
                var candidates = visibleElems.Where(CanPossiblyChangeWorkset).ToList();
                if (candidates.Count == 0)
                {
                    CleanupTempView(doc, workingView, keepTempView);
                    return new { ok = false, msg = "対象ビューにワークセット変更可能なモデル要素がありません。" };
                }

                // -------- 5) ルール適用（先勝ち）→ temp Workset割付プラン作成 --------
                var plan = new Dictionary<ElementId, string>(candidates.Count);
                var hitStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                // Cache Type/Family name lookups by TypeId
                var typeNameCache = new Dictionary<ElementId, (string typeName, string familyName)>();
                foreach (var e in candidates)
                {
                    var ctx = BuildContext(doc, e, baseView, typeNameCache);
                    string ws = null;
                    foreach (var r in rules)
                    {
                        if (RuleMatches(r.When, ctx))
                        {
                            ws = r.WorksetName;
                            if (!hitStats.ContainsKey(ws)) hitStats[ws] = 0;
                            hitStats[ws]++;
                            break;
                        }
                    }
                    ws = ws ?? unmatched;
                    plan[e.Id] = ws;
                    if (!hitStats.ContainsKey(ws)) hitStats[ws] = 0;
                }

                // -------- 6) 一時振替 → DWG → Rollback（全戻し） --------
                string outPathFinal = null;
                int reassigned = 0;
                var createdWorksets = new List<string>();
                var failedIds = new List<int>();

                using (var tg = new TransactionGroup(doc, "DWG Export with Temp Worksets (Atomic)"))
                {
                    tg.Start();

                    // 6-1) 必要なワークセットを用意
                    EnsureWorksets(doc, plan.Values.Distinct(StringComparer.OrdinalIgnoreCase), createdWorksets);

                    // Build workset name map once (avoid repeated collectors)
                    var wsMap = new Dictionary<string, Workset>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var allWs = new FilteredWorksetCollector(doc).ToWorksets();
                        foreach (var ws in allWs)
                        {
                            if (!wsMap.ContainsKey(ws.Name)) wsMap[ws.Name] = ws;
                        }
                    }
                    catch { }

                    // 6-2) 振替（安全に。不可要素はスキップして収集）
                    using (var t = new Transaction(doc, "Assign temp worksets"))
                    {
                        t.Start();
                        foreach (var kv in plan)
                        {
                            var elem = doc.GetElement(kv.Key);
                            if (elem == null) continue;
                            if (!wsMap.TryGetValue(kv.Value, out var target)) { failedIds.Add(kv.Key.IntValue()); continue; }
                            if (target == null) { failedIds.Add(kv.Key.IntValue()); continue; }

                            if (!TrySetElementWorkset(doc, elem, target.Id))
                                failedIds.Add(kv.Key.IntValue());
                            else
                                reassigned++;
                        }
                        t.Commit();
                    }

                    // 6-3) DWG エクスポート（純正の見た目＝Revit 標準の結果）
                    //      ※ ExportDwgCommand と同様の安全命名ロジックを使用（独立実装）
                    var opt = new DWGExportOptions();
                    if (!string.IsNullOrWhiteSpace(setupNameIn))
                        TryApplyDwgExportSetupByName(doc, setupNameIn, opt); // 同名ロジック（このクラス内で実装）
                    if (!string.IsNullOrWhiteSpace(dwgVerIn) && TryParseAcadVersion(dwgVerIn, out var v))
                        opt.FileVersion = v;

                    var baseName = NormalizeBaseName(fileNameIn, workingView);
                    var desired = MakeDwgPath(outputFolder, baseName);

                    bool ok = TryExport(doc, workingView.Id, opt, desired, outputFolder, out var primaryError);
                    if (!ok)
                    {
                        var fallback = MakeFallbackDwgPath(outputFolder, baseView.Id.IntValue(), 1);
                        bool retry = TryExport(doc, workingView.Id, opt, fallback, outputFolder, out var fallbackError);
                        outPathFinal = retry ? fallback : null;

                        CleanupTempView(doc, workingView, keepTempView);
                        tg.RollBack(); // ← 重要：DWG後に必ず全戻し

                        if (!retry)
                        {
                            var reason = JoinNonEmpty(new[]
                            {
                                primaryError != null ? $"primary: {primaryError.Message}" : null,
                                fallbackError != null ? $"fallback: {fallbackError.Message}" : null
                            }, " / ");
                            return new
                            {
                                ok = false,
                                msg = "DWG export failed. " + reason,
                                reassignedCount = reassigned,
                                failedElementIds = failedIds
                            };
                        }

                        return new
                        {
                            ok = true,
                            path = outPathFinal,
                            viewId = baseView.Id.IntValue(),
                            reassignedCount = reassigned,
                            failedElementIds = failedIds,
                            createdWorksets,
                            ruleHits = hitStats,
                            notes = "Primary export failed; fallback name used. All temp changes rolled back."
                        };
                    }

                    outPathFinal = desired;

                    CleanupTempView(doc, workingView, keepTempView);
                    tg.RollBack(); // ← ここで確実に元へ戻す
                }

                return new
                {
                    ok = true,
                    path = outPathFinal,
                    viewId = baseView.Id.IntValue(),
                    reassignedCount = reassigned,
                    failedElementIds = failedIds,
                    createdWorksets,
                    ruleHits = hitStats,
                    notes = "Exported with Workset layer modifier. All temp changes rolled back."
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static void CleanupTempView(Document doc, View workingView, bool keepTempView)
        {
            if (workingView == null || doc == null) return;

            try
            {
                if (keepTempView)
                {
                    // 残す場合は名前だけ分かりやすくして終了
                    using (var t = new Transaction(doc, "Keep temp view (rename)"))
                    {
                        t.Start();
                        try
                        {
                            // 既に削除されていないか確認
                            var v = doc.GetElement(workingView.Id) as View;
                            if (v != null && !v.IsTemplate)
                            {
                                var name = (v.Name ?? "Temp").Trim();
                                if (!name.Contains("[TMP]"))
                                    v.Name = $"{name} [TMP]";
                            }
                        }
                        catch { /* rename 失敗は無視 */ }
                        t.Commit();
                    }
                    return;
                }

                // 破棄（作業用に複製したビューなので安全に削除）
                using (var t = new Transaction(doc, "Delete temp view"))
                {
                    t.Start();
                    try
                    {
                        var v = doc.GetElement(workingView.Id);
                        if (v != null)
                        {
                            doc.Delete(workingView.Id);
                        }
                    }
                    catch
                    {
                        // 既に消えている等は握りつぶし
                    }
                    t.Commit();
                }
            }
            catch
            {
                // ここまで落ちることは稀だが、後続の RollBack に影響しないように無視
            }
        }


        // -----------------------------------------------------------------
        //  ルール定義（最小実装）：when の AND 条件、先勝ち
        // -----------------------------------------------------------------
        private sealed class Rule
        {
            public string WorksetName;
            public WhenClause When;
        }
        private sealed class WhenClause
        {
            public string Category;               // BuiltInCategory 名 or 表示名（曖昧一致可）
            public string TypeRegex;             // ^H-.* など
            public string FamilyRegex;           // S梁 など
            public JObject ParamEquals;          // { "メーカー":"YKK" }
            public JObject ParamContains;        // { "型番":"APW" }
            public string PhaseStatus;           // New, Existing, Demolished 等（簡易）

            // Prepared compiled regex (optional)
            internal Regex TypeCompiled;
            internal Regex FamilyCompiled;
        }

        private static List<Rule> ParseRules(JArray arr)
        {
            var list = new List<Rule>();
            if (arr == null) return list;

            foreach (var jt in arr)
            {
                try
                {
                    var r = new Rule
                    {
                        WorksetName = jt.Value<string>("workset") ?? jt.Value<string>("ws") ?? "WS_UNMATCHED",
                        When = jt["when"]?.ToObject<WhenClause>() ?? new WhenClause()
                    };
                    if (!string.IsNullOrWhiteSpace(r.WorksetName))
                        list.Add(r);
                }
                catch { /* skip bad rule */ }
            }
            return list;
        }

        // Pre-compile regex patterns for rules to reduce per-element overhead
        private static void PrepareRules(List<Rule> rules)
        {
            if (rules == null) return;
            foreach (var r in rules)
            {
                var w = r?.When; if (w == null) continue;
                try { if (!string.IsNullOrWhiteSpace(w.TypeRegex)) w.TypeCompiled = new Regex(w.TypeRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); } catch { w.TypeCompiled = null; }
                try { if (!string.IsNullOrWhiteSpace(w.FamilyRegex)) w.FamilyCompiled = new Regex(w.FamilyRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase); } catch { w.FamilyCompiled = null; }
            }
        }

        // 対象ビュー解決
        private static View ResolveTargetView(Document doc, UIDocument uidoc, JObject p)
        {
            int? viewIdIn = p.Value<int?>("viewId");
            string viewUidIn = p.Value<string>("viewUniqueId");

            if (viewIdIn.HasValue && viewIdIn.Value > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdIn.Value)) as View;

            if (!string.IsNullOrWhiteSpace(viewUidIn))
                return doc.GetElement(viewUidIn) as View;

            return uidoc?.ActiveView;
        }

        // Workset 変更できそうなモデル要素か
        private static bool CanPossiblyChangeWorkset(Element e)
        {
            try
            {
                if (e == null) return false;
                if (e.ViewSpecific) return false; // ビュー固有は不可
                var cat = e.Category;
                if (cat == null) return false;
                var p = e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                return p != null && !p.IsReadOnly;
            }
            catch { return false; }
        }

        // ルール用コンテキスト
        private sealed class Ctx
        {
            public string Category;
            public string TypeName;
            public string FamilyName;
            public string PhaseStatus; // 簡易
            public Func<string, string> GetParam;
        }

        private static Ctx BuildContext(Document doc, Element e, View baseView, System.Collections.Generic.Dictionary<ElementId, (string typeName, string familyName)> typeNameCache) 
        { 
            string typeName = ""; 
            string famName = ""; 
            try 
            { 
                var tid = e.GetTypeId(); 
                if (tid != null && tid != ElementId.InvalidElementId) 
                { 
                    if (!typeNameCache.TryGetValue(tid, out var tf)) 
                    { 
                        var type = e.Document.GetElement(tid) as ElementType; 
                        var tname = type?.Name ?? ""; 
                        var fname = (type as FamilySymbol)?.Family?.Name ?? type?.FamilyName ?? ""; 
                        tf = (tname, fname); 
                        typeNameCache[tid] = tf; 
                    } 
                    typeName = tf.typeName; famName = tf.familyName; 
                } 
            } 
            catch { } 

            // Phase Status（ざっくり取得。ビューのPhaseFilter/Phase による表示は純正側が処理）
            string phase = "";
            try
            {
                var pCreated = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (pCreated != null && pCreated.AsElementId() != ElementId.InvalidElementId)
                {
                    var phaseElem = doc.GetElement(pCreated.AsElementId()) as Phase;
                    phase = phaseElem?.Name ?? "";
                }
            }
            catch { }

            return new Ctx
            {
                Category = e.Category?.Name ?? "",
                TypeName = typeName,
                FamilyName = famName,
                PhaseStatus = phase,
                GetParam = (name) =>
                {
                    try
                    {
                        var param = e.LookupParameter(name);
                        if (param == null) return "";
                        switch (param.StorageType)
                        {
                            case StorageType.Double: return param.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                            case StorageType.Integer: return param.AsInteger().ToString();
                            case StorageType.ElementId: return param.AsElementId().IntValue().ToString();
                            case StorageType.String: return param.AsString() ?? "";
                            default: return "";
                        }
                    }
                    catch { return ""; }
                }
            };
        }

        private static bool RuleMatches(WhenClause w, Ctx c) 
        { 
            if (w == null) return true; 
 
            if (!string.IsNullOrWhiteSpace(w.Category)) 
            { 
                // 部分一致も許容（日本語/英語UI差異対策） 
                if (c.Category.IndexOf(w.Category, StringComparison.OrdinalIgnoreCase) < 0) 
                    return false; 
            } 
            if (!string.IsNullOrWhiteSpace(w.TypeRegex)) 
            { 
                var rx = w.TypeCompiled; 
                if (string.IsNullOrEmpty(c.TypeName)) return false; 
                if (rx != null) { if (!rx.IsMatch(c.TypeName)) return false; } 
                else if (!Regex.IsMatch(c.TypeName, w.TypeRegex, RegexOptions.IgnoreCase)) return false; 
            } 
            if (!string.IsNullOrWhiteSpace(w.FamilyRegex)) 
            { 
                var rx = w.FamilyCompiled; 
                if (string.IsNullOrEmpty(c.FamilyName)) return false; 
                if (rx != null) { if (!rx.IsMatch(c.FamilyName)) return false; } 
                else if (!Regex.IsMatch(c.FamilyName, w.FamilyRegex, RegexOptions.IgnoreCase)) return false; 
            } 
            if (!string.IsNullOrWhiteSpace(w.PhaseStatus))
            {
                if (c.PhaseStatus.IndexOf(w.PhaseStatus, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            if (w.ParamEquals is JObject eqs)
            {
                foreach (var prop in eqs.Properties())
                {
                    var v = c.GetParam?.Invoke(prop.Name) ?? "";
                    if (!string.Equals(v, prop.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            if (w.ParamContains is JObject cons)
            {
                foreach (var prop in cons.Properties())
                {
                    var v = c.GetParam?.Invoke(prop.Name) ?? "";
                    if (v.IndexOf(prop.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
            }
            return true;
        }

        // ---------------- Export / Utility（ExportDwgCommand と整合） ----------------

        private static bool TryExport(Document doc, ElementId viewId, DWGExportOptions opt, string desiredPath, string fallbackFolder, out Exception error)
        {
            error = null;
            try
            {
                string exportFolder = Path.GetDirectoryName(desiredPath) ?? fallbackFolder;
                string exportNameNoEx = Path.GetFileNameWithoutExtension(desiredPath);
                return doc.Export(exportFolder, exportNameNoEx, new List<ElementId> { viewId }, opt);
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static string NormalizeBaseName(string fileNameIn, View workingView)
        {
            string rawBase = !string.IsNullOrWhiteSpace(fileNameIn)
                ? Path.GetFileNameWithoutExtension(fileNameIn)
                : (workingView?.Name ?? "export");
            string safe = SanitizeFileName(rawBase);
            if (string.IsNullOrWhiteSpace(safe)) safe = "export";
            return safe;
        }

        private static string MakeDwgPath(string folder, string baseName)
        {
            return GetNonConflictingPath(folder, baseName, ".dwg");
        }

        private static string MakeFallbackDwgPath(string folder, int viewId, int startFrom = 1, int max = 9999)
        {
            for (int i = startFrom; i <= max; i++)
            {
                string baseName = $"export_{viewId}_{i}";
                string path = Path.Combine(folder, baseName + ".dwg");
                if (!System.IO.File.Exists(path)) return path;
            }
            return Path.Combine(folder, $"export_{viewId}_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");
        }

        private static string GetNonConflictingPath(string folder, string baseName, string ext)
        {
            string safeBase = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "export";

            var path = Path.Combine(folder, safeBase + ext);
            int i = 1;
            while (System.IO.File.Exists(path))
            {
                path = Path.Combine(folder, $"{safeBase} ({i}){ext}");
                i++;
            }
            return path;
        }

        private static string SanitizeElementName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Temp";
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

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "export";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
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

        private static string JoinNonEmpty(IEnumerable<string> parts, string sep)
        {
            var list = parts?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>();
            return string.Join(sep, list);
        }

        // --- Workset helpers ----------------------------------------------------

        private static void EnsureWorksets(Document doc, IEnumerable<string> names, IList<string> createdOut)
        {
            using (var t = new Transaction(doc, "Ensure temp worksets"))
            {
                t.Start();
                foreach (var n in names.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var existing = GetWorksetByName(doc, n);
                    if (existing != null) continue;

                    try
                    {
                        Workset.Create(doc, n); // 2引数版ではなく名前のみ（.NET4.8/Revit2023+）
                        createdOut?.Add(n);
                    }
                    catch
                    {
                        // 作成失敗時は後続で割当時に失敗として扱う
                    }
                }
                t.Commit();
            }
        }

        private static Workset GetWorksetByName(Document doc, string name)
        {
            try
            {
                var coll = new FilteredWorksetCollector(doc).ToWorksets();
                foreach (var ws in coll)
                {
                    if (string.Equals(ws.Name, name, StringComparison.OrdinalIgnoreCase))
                        return ws;
                }
            }
            catch { }
            return null;
        }

        private static bool TrySetElementWorkset(Document doc, Element elem, WorksetId wsId)
        {
            try
            {
                // 推奨：BuiltInParameter 経由（SetElementWorksetCommand と整合）
                var p = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (p != null && !p.IsReadOnly)
                {
                    var ok = p.Set(wsId.IntValue());
                    return ok;
                }
            }
            catch { }

            try
            {
                // 万一のため：WorksetUtils（環境次第で無い可能性もあるので guarded）
                // return WorksetUtils.CheckAndSetOwnerWorksetId(doc, elem.Id, wsId);
                // ↑ 上記を使える環境ならコメントアウト解除
            }
            catch { }

            return false;
        }

        // --- DWG export setup name → options へ適用（反射安全版） ---------------
        private static void TryApplyDwgExportSetupByName(Document doc, string setupName, DWGExportOptions opt)
        {
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
                    var nameParam = e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
                                    ?? e.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)
                                    ?? e.LookupParameter("Name");
                    var n = nameParam?.AsString();
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, setupName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var mi = typeof(DWGExportOptions).GetMethod("SetExportDWGSettings");
                        if (mi != null)
                        {
                            mi.Invoke(opt, new object[] { doc, e.Id });
                        }
                        break;
                    }
                }
            }
            catch
            {
                // 既定オプションで続行
            }
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
    }

}


