// ================================================================
// File: Core/ExportDwgByParamGroupsTick.cs
// Purpose: export_dwg_by_param_groups の 1 tick 分処理（時間枠で小分け）
// Strategy: ビュー複製 → 対象グループ"以外"の要素を一時的に Hide → DWG 出力 → Unhide → 片付け
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes  :
//   - FilterStringRule / ParameterFilterElement は使用しない（環境差の例外対策）
//   - providerId は常に ElementId? を返す（BuiltIn "Comments" / インスタンス param はOK、タイプのみは null）
//   - fallbackOnFilterFailure, fallbackExportIfNoGroups をサポート（必要なら params で切替）
//   - LongOpEngine からは Run(...) の戻りタプルをそのまま利用できる
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class ExportDwgByParamGroupsTick
    {
        internal static (bool ok, List<object> outputs, List<object> skipped, bool done, int? nextIndex, int totalGroups, int processed, int elapsedMs, string phase, string msg)
        Run(UIApplication uiapp, LongOpEngine.Job job, int maxMillis)
        {
            var sw = Stopwatch.StartNew();
            var outputs = new List<object>();
            var skipped = new List<object>();
            string phase = "init";
            int processed = 0;

            var doc = (uiapp.ActiveUIDocument != null) ? uiapp.ActiveUIDocument.Document : null;
            if (doc == null)
                return (false, outputs, skipped, true, null, 0, 0, (int)sw.ElapsedMilliseconds, phase, "No active document.");

            // ---- params ----
            var p = job.Params;
            int viewId = p.Value<int?>("viewId") ?? 0;
            string catName = p.Value<string>("category") ?? "OST_Walls";
            string paramName = p.Value<string>("paramName") ?? "Comments";
            string outFolder = p.Value<string>("outputFolder") ?? "";
            string filePref = p.Value<string>("fileNamePrefix") ?? "ByParam";
            string setup = p.Value<string>("useExportSetup");
            string dwgVer = p.Value<string>("dwgVersion");
            bool keepView = p.Value<bool?>("keepTempView") ?? false;

            // 診断／フォールバック
            bool fallbackExportIfNoGroups = p.Value<bool?>("fallbackExportIfNoGroups") ?? false;
            bool fallbackOnFilterFailure = p.Value<bool?>("fallbackOnFilterFailure") ?? true;

            if (viewId <= 0 || string.IsNullOrWhiteSpace(outFolder))
                return (false, outputs, skipped, true, null, 0, 0, (int)sw.ElapsedMilliseconds, "init", "Bad params.");

            Directory.CreateDirectory(outFolder);

            BuiltInCategory bic = ResolveBic(catName, BuiltInCategory.OST_Walls);
            var baseView = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (baseView == null || !baseView.CanBePrinted)
                return (false, outputs, skipped, true, null, 0, 0, (int)sw.ElapsedMilliseconds, "init", "Target view is not printable.");

            // ---- groups 列挙（速い）----（初回のみ）
            List<string> groups = null;
            IList<Element> visElems = null;
            ElementId? providerId = null;           // ElementId?（null=タイプ側のみ等でフィルタ不可）
            Func<Element, string> reader = null;
            JArray cached = p["__groups"] as JArray;
            if (cached != null)
            {
                // reuse cached groups to avoid re-scanning the view every tick
                phase = "scan(reuse)";
                groups = cached.Values<string>().Where(s => s != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                // resolve provider/reader cheaply from a single sample
                try
                {
                    visElems = new FilteredElementCollector(doc, baseView.Id).OfCategory(bic).WhereElementIsNotElementType().ToElements();
                    var sample = visElems.FirstOrDefault();
                    var rp0 = ResolveProviderId(doc, sample, paramName);
                    providerId = rp0.provider; reader = rp0.reader;
                }
                catch { providerId = null; reader = (e) => null; }
            }
            else
            {
                phase = "scan";
                visElems = new FilteredElementCollector(doc, baseView.Id).OfCategory(bic).WhereElementIsNotElementType().ToElements();
                var rp = ResolveProviderId(doc, visElems.Count > 0 ? visElems[0] : null, paramName);
                providerId = rp.provider;           // ElementId?（null=タイプ側のみ等でフィルタ不可）
                reader = rp.reader;

                groups = (visElems.Count == 0)
                    ? new List<string>()
                    : visElems.Select(e => reader(e) ?? "")
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                              .ToList();

                // cache for subsequent ticks (in-memory Job.Params)
                try
                {
                    p["__groups"] = JArray.FromObject(groups);
                    // Precompute group -> elementIds and all visible ids
                    var allIds = new JArray();
                    var map = new JObject();
                    foreach (var e in visElems)
                    {
                        var id = e.Id.IntValue();
                        allIds.Add(id);
                        var key = reader(e) ?? "";
                        if (map[key] == null) map[key] = new JArray();
                        ((JArray)map[key]).Add(id);
                    }
                    p["__allVisIds"] = allIds;
                    p["__groupIds"] = map;
                }
                catch { }
            }

            int total = groups.Count;
            if (total == 0)
            {
                if (fallbackExportIfNoGroups)
                {
                    var msgFb = ExportWholeViewOnce(doc, baseView, outFolder, filePref + "_FALLBACK", setup, dwgVer, keepView, out string outPathFb, out string errFb);
                    if (msgFb == null) outputs.Add(new { index = -1, group = "<FALLBACK>", path = outPathFb });
                    else skipped.Add(new { index = -1, group = "<FALLBACK>", error = msgFb + (string.IsNullOrEmpty(errFb) ? "" : " | " + errFb) });

                    return (outputs.Count > 0, outputs, skipped, true, null, 0, 1, (int)sw.ElapsedMilliseconds, "scan",
                        (outputs.Count > 0 ? "No groups; fallback export completed." : "No groups; fallback export failed."));
                }

                return (true, outputs, skipped, true, null, 0, 0, (int)sw.ElapsedMilliseconds, "scan", "No groups.");
            }

            int startIndex = Math.Max(0, job.NextIndex);
            if (startIndex >= total)
                return (true, outputs, skipped, true, null, total, 0, (int)sw.ElapsedMilliseconds, "scan", "Already completed.");

            // ---- DWG オプション ----
            var opt = new DWGExportOptions();
            if (!string.IsNullOrWhiteSpace(setup)) TryApplyDwgExportSetupByName(doc, setup, opt);
            ACADVersion ver;
            if (!string.IsNullOrWhiteSpace(dwgVer) && Enum.TryParse<ACADVersion>(dwgVer, true, out ver))
                opt.FileVersion = ver;

            // ---- export（時間/件数で小分け）----
            phase = "export";
            var startMs = sw.ElapsedMilliseconds;
            int maxGroups = Math.Max(1, p.Value<int?>("maxGroups") ?? 1);

            for (int i = startIndex; i < total; i++)
            {
                string g = groups[i];
                string safeGroup = string.IsNullOrEmpty(g) ? "NoValue" : Sanitize(g);

                // 1) 作業ビュー
                View working = null;
                using (var t = new Transaction(doc, "Duplicate view"))
                {
                    t.Start();
                    var dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                    working = (View)doc.GetElement(dupId);
                    working.ViewTemplateId = ElementId.InvalidElementId;
                    working.Name = baseView.Name + " ByParam " + DateTime.Now.ToString("HHmmss");
                    t.Commit();
                }

                // 2) 非対象を一時的に隠す
                ICollection<ElementId> hiddenIds = null;
                using (var t = new Transaction(doc, "Hide elements except group"))
                {
                    t.Start();

                    if (providerId == null)
                    {
                        t.RollBack();
                        CleanupTempView(doc, working, keepView);

                        if (fallbackOnFilterFailure)
                        {
                            var msgFb = ExportWholeViewOnce(doc, baseView, outFolder, filePref + "_FALLBACK", setup, dwgVer, keepView, out string outPathFb, out string errFb);
                            if (msgFb == null) outputs.Add(new { index = i, group = g, path = outPathFb, fallback = true });
                            else skipped.Add(new { index = i, group = g, error = msgFb + (string.IsNullOrEmpty(errFb) ? "" : " | " + errFb), fallback = true });
                        }
                        else
                        {
                            skipped.Add(new { index = i, group = g, error = "Filter parameter not usable for view filters." });
                        }
                        goto NEXT;
                    }

                    // visElems は baseView の可視要素。working は複製直後で同じ要素が見えている。
                    var notMatch = new List<ElementId>();
                    var groupIdsCache = p["__groupIds"] as JObject;
                    var allVisIdsCache = p["__allVisIds"] as JArray;
                    if (groupIdsCache != null && allVisIdsCache != null)
                    {
                        var keep = (groupIdsCache[g] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
                        foreach (var idInt in allVisIdsCache.Values<int>())
                        {
                            if (!keep.Contains(idInt)) notMatch.Add(Autodesk.Revit.DB.ElementIdCompat.From(idInt));
                        }
                    }
                    else
                    {
                        for (int k = 0; k < visElems.Count; k++)
                        {
                            var eVis = visElems[k];
                            if (eVis == null) continue;
                            var val = reader(eVis) ?? "";
                            if (!string.Equals(val, g ?? "", StringComparison.OrdinalIgnoreCase))
                                notMatch.Add(eVis.Id);
                        }
                    }

                    hiddenIds = notMatch;
                    if (hiddenIds.Count > 0)
                        working.HideElements(hiddenIds);

                    t.Commit();
                }

                // 3) エクスポート
                string baseName = filePref + "_" + safeGroup;
                string path = GetNonConflictPath(outFolder, baseName, ".dwg");

                bool ok = false;
                string err = null;
                try { ok = doc.Export(outFolder, Path.GetFileNameWithoutExtension(path), new List<ElementId> { working.Id }, opt); }
                catch (Exception ex) { err = ex.Message; }

                // 4) 片付け（Unhide → ビュー破棄 or rename）
                using (var t = new Transaction(doc, "Cleanup hidden & view"))
                {
                    t.Start();
                    try { if (hiddenIds != null && hiddenIds.Count > 0) working.UnhideElements(hiddenIds); } catch { }
                    t.Commit();
                }
                CleanupTempView(doc, working, keepView);

                if (ok) outputs.Add(new { index = i, group = g, value = g, path });
                else skipped.Add(new { index = i, group = g, error = (err ?? "export failed") });

                NEXT:
                processed++;
                if (processed >= maxGroups) break;
                if ((sw.ElapsedMilliseconds - startMs) >= maxMillis) break;
            }

            bool done = (startIndex + processed) >= total;
            int? next = done ? (int?)null : (startIndex + processed);
            job.NextIndex = next.HasValue ? next.Value : job.NextIndex;

            return (outputs.Count > 0 || done, outputs, skipped, done, next, total, processed, (int)sw.ElapsedMilliseconds, phase,
                done ? "All groups processed." : "Partial processed.");
        }

        // ---------------- helpers ----------------

        private static string Sanitize(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            var cleaned = sb.ToString().Trim();
            if (cleaned.EndsWith(".")) cleaned = cleaned.TrimEnd('.');
            if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "export";
            return cleaned;
        }

        private static string GetNonConflictPath(string folder, string baseName, string ext)
        {
            string path = Path.Combine(folder, baseName + ext);
            int i = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, baseName + " (" + i + ")" + ext);
                i++;
            }
            return path;
        }

        private static BuiltInCategory ResolveBic(string s, BuiltInCategory fallback)
        {
            BuiltInCategory v;
            if (Enum.TryParse<BuiltInCategory>(s, true, out v)) return v;
            if (string.Equals(s, "Walls", StringComparison.OrdinalIgnoreCase)) return BuiltInCategory.OST_Walls;
            return fallback;
        }

        /// <summary>
        /// フィルタに使うパラメータID（ElementId?）と、値読み取り関数（インスタンス→タイプ順）を返す。
        /// </summary>
        private static (ElementId? provider, Func<Element, string> reader)
        ResolveProviderId(Document doc, Element sample, string paramName)
        {
            ElementId? provider = null;
            Func<Element, string> r = e => null;

            // BuiltInParameter: Comments / コメント（インスタンス）
            if (string.Equals(paramName, "Comments", StringComparison.OrdinalIgnoreCase) || paramName == "コメント")
            {
                provider = Autodesk.Revit.DB.ElementIdCompat.From((long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                r = e =>
                {
                    var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    return p != null ? p.AsString() : null;
                };
                return (provider, r);
            }

            if (sample != null)
            {
                // インスタンス側
                var inst = sample.LookupParameter(paramName);
                if (inst != null)
                {
                    provider = inst.Id; // ElementId
                    r = e =>
                    {
                        try
                        {
                            var p = e.LookupParameter(paramName);
                            if (p == null) return null;
                            switch (p.StorageType)
                            {
                                case StorageType.String: return p.AsString();
                                case StorageType.Integer: return p.AsInteger().ToString();
                                case StorageType.Double: return p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                                case StorageType.ElementId: return p.AsElementId().IntValue().ToString();
                            }
                        }
                        catch { }
                        return null;
                    };
                    return (provider, r);
                }

                // タイプ側のみ（フィルタ不可 → provider=null）
                var et = doc.GetElement(sample.GetTypeId()) as ElementType;
                var typ = et != null ? et.LookupParameter(paramName) : null;
                if (typ != null)
                {
                    provider = null;
                    r = e =>
                    {
                        try
                        {
                            var t = doc.GetElement(e.GetTypeId()) as ElementType;
                            var p2 = t != null ? t.LookupParameter(paramName) : null;
                            if (p2 == null) return null;
                            switch (p2.StorageType)
                            {
                                case StorageType.String: return p2.AsString();
                                case StorageType.Integer: return p2.AsInteger().ToString();
                                case StorageType.Double: return p2.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                                case StorageType.ElementId: return p2.AsElementId().IntValue().ToString();
                            }
                        }
                        catch { }
                        return null;
                    };
                    return (provider, r);
                }
            }

            return (provider, r);
        }

        private static bool TryApplyDwgExportSetupByName(Document doc, string setupName, DWGExportOptions opt)
        {
            try
            {
                var elems = new FilteredElementCollector(doc).OfClass(typeof(Element)).ToElements();
                foreach (var e in elems)
                {
                    var n = e.Name;
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, setupName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var mi = typeof(DWGExportOptions).GetMethod("SetExportDWGSettings");
                        if (mi != null) mi.Invoke(opt, new object[] { doc, e.Id });
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>ビュー全体を1枚だけ出力（診断/フォールバック用）</summary>
        private static string ExportWholeViewOnce(Document doc, View baseView, string outFolder, string filePref, string setup, string dwgVer, bool keepView, out string outPath, out string errMsg)
        {
            errMsg = null;
            outPath = null;

            var opt = new DWGExportOptions();
            if (!string.IsNullOrWhiteSpace(setup)) TryApplyDwgExportSetupByName(doc, setup, opt);
            ACADVersion ver;
            if (!string.IsNullOrWhiteSpace(dwgVer) && Enum.TryParse<ACADVersion>(dwgVer, true, out ver))
                opt.FileVersion = ver;

            View working = null;
            using (var t = new Transaction(doc, "Duplicate view (whole)"))
            {
                t.Start();
                var dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                working = (View)doc.GetElement(dupId);
                working.ViewTemplateId = ElementId.InvalidElementId;
                working.Name = baseView.Name + " Whole " + DateTime.Now.ToString("HHmmss");
                t.Commit();
            }

            string baseName = filePref;
            outPath = GetNonConflictPath(outFolder, baseName, ".dwg");

            bool ok = false;
            try { ok = doc.Export(outFolder, Path.GetFileNameWithoutExtension(outPath), new List<ElementId> { working.Id }, opt); }
            catch (Exception ex) { errMsg = ex.Message; }

            CleanupTempView(doc, working, keepView);

            return ok ? null : (errMsg ?? "Export failed (whole view).");
        }

        private static void CleanupTempView(Document doc, View working, bool keep)
        {
            try
            {
                if (keep)
                {
                    using (var t = new Transaction(doc, "Keep temp view (rename)"))
                    {
                        t.Start();
                        try
                        {
                            if (working != null && !working.IsTemplate)
                            {
                                var nm = (working.Name ?? "Temp").Trim();
                                if (nm.IndexOf("[TMP]", StringComparison.OrdinalIgnoreCase) < 0)
                                    working.Name = nm + " [TMP]";
                            }
                        }
                        catch { }
                        t.Commit();
                    }
                }
                else
                {
                    using (var t = new Transaction(doc, "Delete temp view"))
                    {
                        t.Start();
                        try { if (working != null) doc.Delete(working.Id); } catch { }
                        t.Commit();
                    }
                }
            }
            catch { }
        }
    }
}



