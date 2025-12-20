// Patched version: display-text oriented CSV exporter with regenerate & header strategy
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class ExportScheduleToCsvCommand : IRevitCommandHandler
    {
        public string CommandName => "export_schedule_to_csv";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)(cmd.Params ?? new JObject());

                int scheduleViewId = p.Value<int?>("scheduleViewId") ?? 0;
                if (scheduleViewId <= 0)
                    return ResultUtil.Err("scheduleViewId is required");

                string path = p.Value<string>("outputFilePath")
                              ?? throw new ArgumentException("outputFilePath is required");

                bool includeHeader = p.Value<bool?>("includeHeader") ?? true;
                string delimiter = string.IsNullOrEmpty(p.Value<string>("delimiter")) ? "," : p.Value<string>("delimiter");
                string newline = string.IsNullOrEmpty(p.Value<string>("newline")) ? Environment.NewLine : p.Value<string>("newline");

                bool fillBlanks = p.Value<bool?>("fillBlanks") ?? false; // opt-in
                bool itemize = p.Value<bool?>("itemize") ?? false;       // opt-in; best-effort

                var doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                    return ResultUtil.Err("No active document.");

                var vs = doc.GetElement(new ElementId(scheduleViewId)) as ViewSchedule;
                if (vs == null)
                    return ResultUtil.Err($"ScheduleView {scheduleViewId} not found.");

                // ---------- (optional) itemize: duplicate, itemize, clear grouping ----------
                ViewSchedule workVs = vs;
                ElementId tempId = ElementId.InvalidElementId;
                if (itemize)
                {
                    using (var tx = new Transaction(doc, "ExportScheduleToCsv - itemize duplicate"))
                    {
                        tx.Start();
                        try
                        {
                            var dupId = vs.Duplicate(ViewDuplicateOption.Duplicate);
                            tempId = dupId;
                            workVs = doc.GetElement(dupId) as ViewSchedule;

                            var def = workVs?.Definition;
                            try { def.IsItemized = true; } catch { /* versions */ }
                            try { def.ShowGrandTotal = false; } catch { /* 2023/2024 */ }
                            TrySetPropertyIfExists(def, "ShowGrandTotals", false); // 将来のAPI名揺れに備え

                            try
                            {
                                while (def.GetSortGroupFieldCount() > 0)
                                    def.RemoveSortGroupField(0);
                            }
                            catch { /* ignore */ }

                            // 変更反映
                            doc.Regenerate();
                        }
                        catch { /* ignore */ }
                        tx.Commit();
                    }
                }

                // ---------- 読み取り直前に Regenerate を明示（取りこぼし防止） ----------
                using (var tx = new Transaction(doc, "ExportScheduleToCsv - Regenerate"))
                {
                    tx.Start();
                    doc.Regenerate();
                    tx.Commit();
                }

                // ---------- Table data ----------
                var table = workVs.GetTableData();
                var bodySec = table.GetSectionData(SectionType.Body);
                if (!IsValidSection(bodySec))
                {
                    CleanupTemp(doc, tempId);
                    return ResultUtil.Err("The schedule Body section has no rows/columns to export.");
                }

                // “見たまま”値取得のため、ViewSchedule.GetCellText を優先する
                // セル走査は 0..NumberOfRows-1 / 0..NumberOfColumns-1 の 0 基点で扱う
                int rows = bodySec.NumberOfRows;
                int cols = bodySec.NumberOfColumns;

                // ---------- ヘッダ1行の構築（表示名 ≫ 定義名(Hidden除外) ≫ Headerセクション） ----------
                string[] headerRow = Array.Empty<string>();
                if (includeHeader)
                {
                    var def = workVs.Definition;
                    var fieldOrder = def.GetFieldOrder();
                    headerRow = new string[cols];

                    for (int c = 0; c < cols; c++)
                    {
                        string header = string.Empty;

                        // 1) Body最上段(0行目)を列見出し候補として試す（案件によりここに見出しが来る）
                        try { header = workVs.GetCellText(SectionType.Body, 0, c) ?? ""; } catch { header = ""; }

                        // 2) 取れない場合は Definition の名前（Hiddenは採用しない）
                        if (string.IsNullOrWhiteSpace(header) && c < fieldOrder.Count)
                        {
                            try
                            {
                                var sf = def.GetField(fieldOrder[c]);
                                if (sf != null && !sf.IsHidden)
                                    header = sf.GetName() ?? "";
                            }
                            catch { /* ignore */ }
                        }

                        // 3) それでも空なら、Headerセクションを下から探索
                        if (string.IsNullOrWhiteSpace(header))
                        {
                            try
                            {
                                var headerSec = table.GetSectionData(SectionType.Header);
                                int hr = headerSec.NumberOfRows;
                                for (int r = hr - 1; r >= 0; r--)
                                {
                                    var h = workVs.GetCellText(SectionType.Header, r, c) ?? "";
                                    if (!string.IsNullOrWhiteSpace(h)) { header = h; break; }
                                }
                            }
                            catch { /* ignore */ }
                        }

                        headerRow[c] = header;
                    }
                }

                // ---------- 本体：表示文字列（GetCellText 3引数優先、保険で TableSectionData.GetCellText） ----------
                var bodyTab = ReadSectionDisplay(workVs, bodySec, SectionType.Body);

                if (fillBlanks && bodyTab.Length > 0)
                    FillBlanksDownwards(bodyTab);

                // ---------- 書き出し（UTF-8 BOM / 指定の改行コード） ----------
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                {
                    sw.NewLine = newline;

                    if (includeHeader)
                        sw.WriteLine(string.Join(delimiter, headerRow.Select(EscapeCsv)));

                    foreach (var row in bodyTab)
                        sw.WriteLine(string.Join(delimiter, row.Select(EscapeCsv)));
                }

                // ---------- 一時ビュー片付け ----------
                CleanupTemp(doc, tempId);

                return new { ok = true, path, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        // ===== helpers =====

        private static void CleanupTemp(Document doc, ElementId tempId)
        {
            if (tempId == ElementId.InvalidElementId) return;
            using (var tx = new Transaction(doc, "ExportScheduleToCsv - cleanup"))
            {
                tx.Start();
                try { doc.Delete(tempId); } catch { /* ignore */ }
                tx.Commit();
            }
        }

        private static bool IsValidSection(TableSectionData sec)
        {
            if (sec == null) return false;
            try
            {
                // 0基点の行列数で評価
                return sec.NumberOfRows > 0 && sec.NumberOfColumns > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// “見たまま”表示文字列を優先してセクションを読み取る。
        /// 優先: ViewSchedule.GetCellText(section, r, c) / 保険: TableSectionData.GetCellText(rowAbs, colAbs)
        /// </summary>
        private static string[][] ReadSectionDisplay(ViewSchedule vs, TableSectionData sec, SectionType sectionType)
        {
            int rows = sec.NumberOfRows;
            int cols = sec.NumberOfColumns;
            var table = new string[rows][];

            // Absolute index（保険用）
            int r0 = sec.FirstRowNumber;
            int c0 = sec.FirstColumnNumber;

            for (int r = 0; r < rows; r++)
            {
                var row = new string[cols];
                for (int c = 0; c < cols; c++)
                {
                    string s = "";
                    try { s = vs.GetCellText(sectionType, r, c) ?? ""; }
                    catch
                    {
                        try { s = sec.GetCellText(r0 + r, c0 + c) ?? ""; }
                        catch { s = ""; }
                    }
                    row[c] = s;
                }
                table[r] = row;
            }
            return table;
        }

        private static void FillBlanksDownwards(string[][] table)
        {
            if (table.Length == 0) return;
            int cols = table[0].Length;
            for (int c = 0; c < cols; c++)
            {
                string last = null;
                for (int r = 0; r < table.Length; r++)
                {
                    var v = table[r][c];
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        last = v;
                    }
                    else if (!string.IsNullOrWhiteSpace(last))
                    {
                        table[r][c] = last;
                    }
                }
            }
        }

        private static string EscapeCsv(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            bool needQuote = v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return needQuote ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        private static void TrySetPropertyIfExists(object target, string propName, object? value)
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
