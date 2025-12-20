// ================================================================
// File: Commands/Schedules/ExportScheduleToXlsxClosedXmlV3Command.cs
// Purpose: Export a ViewSchedule to .xlsx using ClosedXML (no Excel required)
// Target : .NET Framework 4.8 (Revit 2023/2024)
// NuGet  : ClosedXML (e.g., 0.95.4)
// Notes  : - 表示文字列は ViewSchedule.GetCellText(SectionType,row,col) を優先
//          - フォールバックとして TableSectionData.GetCellText(row,col)
//          - 読み取り直前に tx.Start(); doc.Regenerate(); tx.Commit();
// Params : { "viewId"?:int, "viewName"?:string, "filePath"?:string, "autoFit"?:bool }
// Return : { ok:bool, path?:string, msg?:string, detail?:string }
// ================================================================
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;              // IRevitCommandHandler / RequestCommand / RevitLogger
using ClosedXML.Excel;

namespace RevitMCPAddin.Commands.Schedules
{
    public class ExportScheduleToExcelCommand : IRevitCommandHandler
    {
        public string CommandName => "export_schedule_to_excel";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---- Resolve schedule view ----
            ViewSchedule? vs = null;

            var viewId = p.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0)
            {
                vs = doc.GetElement(new ElementId(viewId.Value)) as ViewSchedule;
            }
            if (vs == null)
            {
                var viewName = p.Value<string>("viewName");
                if (!string.IsNullOrWhiteSpace(viewName))
                {
                    vs = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .FirstOrDefault(v => !v.IsTemplate &&
                            string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (vs == null)
                return new { ok = false, msg = "ViewSchedule not found. Provide 'viewId' or 'viewName'." };

            // ---- Output path ----
            string filePath = p.Value<string>("filePath") ?? "";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                filePath = Path.Combine(Path.GetTempPath(), $"Schedule_{vs.Id.IntegerValue}_{ts}.xlsx");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            bool autoFit = p.Value<bool?>("autoFit") ?? true;

            try
            {
                // ---- 強制再生成（取りこぼし防止）----
                using (var tx = new Transaction(doc, "Export Schedule – Regenerate"))
                {
                    tx.Start();
                    doc.Regenerate();
                    tx.Commit();
                }

                // ---- Table data 取得（再生成後）----
                var tableData = vs.GetTableData();
                var body = tableData.GetSectionData(SectionType.Body);
                int rows = body.NumberOfRows;
                int cols = body.NumberOfColumns;

                var def = vs.Definition;
                IList<ScheduleFieldId> fieldOrder = def.GetFieldOrder();

                using (var wb = new XLWorkbook(XLEventTracking.Disabled))
                {
                    var ws = wb.AddWorksheet("Schedule");

                    // ---- Header row (1): 表示ヘッダ優先 → 定義名 → Headerセクション ----
                    for (int j = 0; j < cols; j++)
                    {
                        string header = string.Empty;

                        // 1) Bodyの最上段を試す（案件によりここに列見出しが出る）
                        try { header = vs.GetCellText(SectionType.Body, 0, j) ?? ""; } catch { header = ""; }

                        // 2) 取れなければ Definition のフィールド名（Hiddenは使わない）
                        if (string.IsNullOrWhiteSpace(header) && j < fieldOrder.Count)
                        {
                            try
                            {
                                var sf = def.GetField(fieldOrder[j]);
                                if (sf != null && !sf.IsHidden)
                                    header = sf.GetName() ?? "";
                            }
                            catch { /* ignore */ }
                        }

                        // 3) 最後の保険: Header セクションの下側から探索
                        if (string.IsNullOrWhiteSpace(header))
                        {
                            try
                            {
                                var headerSec = tableData.GetSectionData(SectionType.Header);
                                int hr = headerSec.NumberOfRows;
                                for (int r = hr - 1; r >= 0; r--)
                                {
                                    var h = vs.GetCellText(SectionType.Header, r, j) ?? "";
                                    if (!string.IsNullOrWhiteSpace(h)) { header = h; break; }
                                }
                            }
                            catch { /* ignore */ }
                        }

                        ws.Cell(1, j + 1).Value = header;
                    }

                    // header style
                    var headerRange = ws.Range(1, 1, 1, Math.Max(1, cols));
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                    // ---- Body rows (2行目から) : 表示文字列（優先: ViewSchedule.GetCellText）----
                    for (int i = 0; i < rows; i++)
                    {
                        for (int j = 0; j < cols; j++)
                        {
                            string txt;
                            try
                            {
                                // “見たまま”を最優先（計算値や書式込みに強い）
                                txt = vs.GetCellText(SectionType.Body, i, j) ?? "";
                            }
                            catch
                            {
                                // 保険：TableSectionData 経由（型により空文字もあり得る）
                                try { txt = body.GetCellText(i, j) ?? ""; }
                                catch { txt = ""; }
                            }
                            ws.Cell(i + 2, j + 1).Value = txt;
                        }
                    }

                    // ---- Borders ----
                    int lastRow = Math.Max(1, rows + 1);
                    int lastCol = Math.Max(1, cols);
                    var allRange = ws.Range(1, 1, lastRow, lastCol);
                    allRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    allRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    // ---- Optional: AutoFit ----
                    if (autoFit)
                    {
                        try { ws.Columns(1, lastCol).AdjustToContents(); } catch { /* ignore */ }
                    }

                    // ---- Save ----
                    wb.SaveAs(filePath);
                }

                return new { ok = true, path = filePath };
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"ClosedXML export failed (V3): {ex}");
                return new { ok = false, msg = "ClosedXML export failed.", detail = ex.Message };
            }
        }
    }
}
