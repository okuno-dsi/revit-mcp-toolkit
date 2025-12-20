// RevitMCPAddin/Commands/ScheduleOps/GetScheduleDataCommand.cs (Safe+Fixed版)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    public class GetScheduleDataCommand : IRevitCommandHandler
    {
        public string CommandName => "get_schedule_data";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)(cmd.Params ?? new JObject());
                int id = p.Value<int>("scheduleViewId");
                int skip = Math.Max(0, p.Value<int?>("skip") ?? 0);
                int count = Math.Max(0, p.Value<int?>("count") ?? 1000);

                var doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null)
                    return ResultUtil.Err("No active document.");

                var vs = doc.GetElement(new ElementId(id)) as ViewSchedule;
                if (vs == null)
                    return new { ok = false, message = $"ScheduleView {id} not found.", units = UnitHelper.DefaultUnitsMeta() };

                // ---- 読み取り直前に Regenerate を明示（取りこぼし防止）----
                using (var tx = new Transaction(doc, "GetScheduleData – Regenerate"))
                {
                    tx.Start();
                    doc.Regenerate();
                    tx.Commit();
                }

                // 取得対象は Body セクションのみ（Title は API で扱えない、Header は列名なら別処理）
                var tableData = vs.GetTableData();
                var body = tableData.GetSectionData(SectionType.Body);
                if (!IsValidSection(body))
                    return ResultUtil.Err("The schedule Body section has no rows/columns to read.");

                var def = vs.Definition;

                // 表示順のフィールド（非表示は除外）を取得
                var visibleFields = def.GetFieldOrder()
                                       .Select(fid => def.GetField(fid))
                                       .Where(sf => sf != null && !sf.IsHidden)
                                       .ToList();

                // ボディの列範囲（0基点の列数と一致する想定だが防御的に算出）
                int totalRows = body.NumberOfRows;      // セクション内相対行数
                int totalCols = body.NumberOfColumns;   // セクション内相対列数
                int colStartAbs = body.FirstColumnNumber;
                int rowStartAbs = body.FirstRowNumber;

                // 可視フィールド数とボディ列数の小さい方を使う（列・フィールドずれ対策）
                int visibleColCount = Math.Min(visibleFields.Count, totalCols);
                if (visibleColCount <= 0)
                    return ResultUtil.Err("No visible columns to read in schedule Body.");

                // ページング調整
                if (skip >= totalRows) skip = totalRows;
                int take = Math.Min(count, Math.Max(0, totalRows - skip));

                var rows = new List<Dictionary<string, object>>(take);

                for (int rRel = skip; rRel < skip + take; rRel++) // rRel: セクション内相対行
                {
                    var row = new Dictionary<string, object>(visibleColCount);
                    for (int i = 0; i < visibleColCount; i++)
                    {
                        int cRel = i; // セクション内相対列
                        int rAbs = rowStartAbs + rRel;          // ← 修正: TableSectionData は絶対行
                        int cAbs = colStartAbs + cRel;          // ← 絶対列

                        string fieldName;
                        try
                        {
                            // def のフィールド名（内部定義）を列名にする
                            fieldName = visibleFields[i].GetName();
                        }
                        catch
                        {
                            fieldName = $"Column{i + 1}";
                        }

                        string cellText = "";
                        try
                        {
                            // 1) 最優先: “見たまま”表示文字列（相対インデックス）
                            cellText = vs.GetCellText(SectionType.Body, rRel, cRel) ?? "";
                        }
                        catch
                        {
                            // 2) 保険: TableSectionData 経由（絶対インデックスが必要）
                            try { cellText = body.GetCellText(rAbs, cAbs) ?? ""; } catch { cellText = ""; }
                        }

                        row[fieldName] = cellText;
                    }
                    rows.Add(row);
                }

                return new
                {
                    ok = true,
                    rows,
                    totalCount = totalRows,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        private static bool IsValidSection(TableSectionData sec)
        {
            if (sec == null) return false;
            try
            {
                // NumberOfRows/Columns が 0 より大きいかを使う方が安定
                return sec.NumberOfRows > 0 && sec.NumberOfColumns > 0;
            }
            catch { return false; }
        }
    }
}
