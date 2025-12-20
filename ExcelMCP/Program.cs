using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Drawing.Charts;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

LogUtil.Init();
LogUtil.Info("[ExcelMCP] Building application...");
var app = builder.Build();
LogUtil.Info($"[ExcelMCP] Environment: {app.Environment.EnvironmentName}");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Simple request logging middleware
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    LogUtil.Info($"[REQ] {ctx.Request.Method} {ctx.Request.Path}");
    try
    {
        await next();
        LogUtil.Info($"[RES] {ctx.Request.Method} {ctx.Request.Path} {ctx.Response?.StatusCode} {sw.ElapsedMilliseconds}ms");
    }
    catch (Exception ex)
    {
        LogUtil.Error($"[ERR] {ctx.Request.Method} {ctx.Request.Path} -> {ex.GetType().Name}: {ex.Message}");
        throw;
    }
});

app.MapGet("/", () => Results.Ok(new { ok = true, name = "ExcelMCP", message = "Server is running" }));
app.MapGet("/health", () => new { ok = true, name = "ExcelMCP", time = DateTime.UtcNow });

// List currently open Excel workbooks via COM (if Excel is running)
app.MapGet("/list_open_workbooks", () =>
{
    try
    {
        object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
        if (excelObj is null)
            return Results.Ok(new { ok = true, found = false, workbooks = Array.Empty<object>(), message = "Excel is not running" });

        var result = new List<object>();
        dynamic excel = excelObj;
        try
        {
            dynamic wbs = excel.Workbooks;
            int count = 0;
            try { count = (int)wbs.Count; } catch { count = 0; }
            for (int i = 1; i <= count; i++)
            {
                dynamic wb = null!;
                try { wb = wbs.Item(i); } catch { continue; }

                string? name = null;
                string? fullName = null;
                string? path = null;
                bool? saved = null;
                try { name = (string)wb.Name; } catch { }
                try { fullName = (string)wb.FullName; } catch { }
                try { path = (string)wb.Path; } catch { }
                try { saved = (bool)wb.Saved; } catch { }

                result.Add(new { name, fullName, path, saved });

                try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
            }
            try { Marshal.ReleaseComObject(wbs); } catch { }
        }
        finally
        {
            try { Marshal.ReleaseComObject(excel); } catch { }
        }

        return Results.Ok(new { ok = true, found = result.Count > 0, workbooks = result });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Read cells from an open workbook via COM
app.MapPost("/com/read_cells", (ComExcelIO.ReadCellsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.RangeA1))
        return Results.BadRequest(new { ok = false, msg = "rangeA1 is required" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    dynamic? rng = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        rng = ws.Range(req.RangeA1);
        object? v = null;
        try { v = req.UseValue2 ? rng.Value2 : rng.Value; }
        catch { v = rng.Value2; }

        var rows = ComUtil.ToJagged(v);
        return Results.Ok(new { ok = true, rows });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (rng != null) Marshal.ReleaseComObject(rng); } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Write cells to an open workbook via COM
app.MapPost("/com/write_cells", (ComExcelIO.WriteCellsRequest req) =>
{
    if (req.Values is null || req.Values.Count == 0)
        return Results.BadRequest(new { ok = false, msg = "values is required (2D array)" });
    if (string.IsNullOrWhiteSpace(req.StartCell))
        return Results.BadRequest(new { ok = false, msg = "startCell is required" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    dynamic? start = null;
    dynamic? end = null;
    dynamic? target = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        int rows = req.Values.Count;
        int cols = req.Values.Max(r => r?.Count ?? 0);
        if (cols <= 0)
            return Results.Ok(new { ok = false, msg = "values has zero columns" });

        start = ws.Range(req.StartCell);
        int startRow = (int)start.Row;
        int startCol = (int)start.Column;
        end = ws.Cells[startRow + rows - 1, startCol + cols - 1];
        target = ws.Range(start, end);

        object[,] box = ComUtil.To2DArray(req.Values, rows, cols);
        try { target.Value2 = box; }
        catch { target.Value = box; }

        return Results.Ok(new { ok = true, wroteRows = rows, wroteCols = cols });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (target != null) Marshal.ReleaseComObject(target); } catch { }
        try { if (end != null) Marshal.ReleaseComObject(end); } catch { }
        try { if (start != null) Marshal.ReleaseComObject(start); } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Save or SaveAs an open workbook via COM
app.MapPost("/com/save_workbook", (ComExcelIO.SaveWorkbookRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        if (!string.IsNullOrWhiteSpace(req.SaveAsFullName))
        {
            wb.SaveAs(req.SaveAsFullName);
        }
        else
        {
            wb.Save();
        }
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Append rows at the first empty row under StartColumn
app.MapPost("/com/append_rows", (ComExcelIO.AppendRowsRequest req) =>
{
    if (req.Rows is null || req.Rows.Count == 0)
        return Results.BadRequest(new { ok = false, msg = "rows is required (2D array)" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    dynamic? c1 = null;
    dynamic? c2 = null;
    dynamic? target = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        int startCol = 1;
        if (!string.IsNullOrWhiteSpace(req.StartColumn))
        {
            try { startCol = ExcelIO.ColumnFromA1(req.StartColumn!); } catch { startCol = 1; }
        }

        int rows = req.Rows.Count;
        int cols = req.Rows.Max(r => r?.Count ?? 0);
        if (cols <= 0)
            return Results.Ok(new { ok = false, msg = "rows has zero columns" });

        // Find next row in startCol
        int totalRows = 0;
        try { totalRows = (int)ws.Rows.Count; } catch { totalRows = 1048576; }
        int lastRow;
        try { lastRow = (int)ws.Cells[totalRows, startCol].End(-4162).Row; } // xlUp = -4162
        catch { lastRow = 1; }
        int nextRow;
        try
        {
            bool cell1Empty = false;
            try
            {
                var v = ws.Cells[1, startCol].Value2;
                cell1Empty = v is null || (v is string s && string.IsNullOrEmpty(s));
            }
            catch { cell1Empty = true; }
            nextRow = (lastRow == 1 && cell1Empty) ? 1 : (lastRow + 1);
        }
        catch { nextRow = lastRow + 1; }

        c1 = ws.Cells[nextRow, startCol];
        c2 = ws.Cells[nextRow + rows - 1, startCol + cols - 1];
        target = ws.Range(c1, c2);

        object[,] box = ComUtil.To2DArray(req.Rows, rows, cols);
        try { target.Value2 = box; }
        catch { target.Value = box; }

        return Results.Ok(new { ok = true, appendedRows = rows, toRow = nextRow + rows - 1 });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (target != null) Marshal.ReleaseComObject(target); } catch { }
        try { if (c2 != null) Marshal.ReleaseComObject(c2); } catch { }
        try { if (c1 != null) Marshal.ReleaseComObject(c1); } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Format a range
app.MapPost("/com/format_range", (ComExcelIO.FormatRangeRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Target))
        return Results.BadRequest(new { ok = false, msg = "target is required (A1 range)" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    dynamic? rng = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });
        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });
        rng = ws.Range(req.Target);

        if (!string.IsNullOrWhiteSpace(req.NumberFormat))
        {
            try { rng.NumberFormat = req.NumberFormat; }
            catch { try { rng.NumberFormatLocal = req.NumberFormat; } catch { } }
        }
        if (!string.IsNullOrWhiteSpace(req.HorizontalAlign))
        {
            try { rng.HorizontalAlignment = ComUtil.HAlign(req.HorizontalAlign); } catch { }
        }
        if (!string.IsNullOrWhiteSpace(req.VerticalAlign))
        {
            try { rng.VerticalAlignment = ComUtil.VAlign(req.VerticalAlign); } catch { }
        }
        if (req.WrapText.HasValue) { try { rng.WrapText = req.WrapText.Value; } catch { } }

        if (!string.IsNullOrWhiteSpace(req.FontName)) { try { rng.Font.Name = req.FontName; } catch { } }
        if (req.FontSize.HasValue) { try { rng.Font.Size = req.FontSize.Value; } catch { } }
        if (req.Bold.HasValue) { try { rng.Font.Bold = req.Bold.Value; } catch { } }
        if (req.Italic.HasValue) { try { rng.Font.Italic = req.Italic.Value; } catch { } }
        if (!string.IsNullOrWhiteSpace(req.FontColor)) { try { rng.Font.Color = ComUtil.ColorFromHex(req.FontColor!); } catch { } }
        if (!string.IsNullOrWhiteSpace(req.FillColor)) { try { rng.Interior.Color = ComUtil.ColorFromHex(req.FillColor!); } catch { } }

        if (req.ColumnWidth.HasValue) { try { rng.ColumnWidth = req.ColumnWidth.Value; } catch { } }
        if (req.RowHeight.HasValue) { try { rng.RowHeight = req.RowHeight.Value; } catch { } }
        if (req.AutoFitColumns == true) { try { rng.Columns.AutoFit(); } catch { } }
        if (req.AutoFitRows == true) { try { rng.Rows.AutoFit(); } catch { } }

        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (rng != null) Marshal.ReleaseComObject(rng); } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Merge vertical header cells in a 3-row header (rows 1..3 by default).
// Rule per column:
//  - If Row1 has text and Row2 is blank: merge 1..3 when Row3 blank, else merge 1..2.
//  - Else if Row2 has text and Row3 is blank: merge 2..3.
// Already merged cells are never re-merged (skip any merge that would include a merged cell).
app.MapPost("/com/merge_vertical_header_cells", (ComExcelIO.MergeVerticalHeaderCellsRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;

    string ColLetters(int col)
    {
        var s = string.Empty;
        while (col > 0)
        {
            int rem = (col - 1) % 26;
            s = (char)('A' + rem) + s;
            col = (col - 1) / 26;
        }
        return s;
    }

    bool HasText(dynamic cell)
    {
        try
        {
            string t = (string?)cell.Text ?? string.Empty;
            return !string.IsNullOrWhiteSpace(t);
        }
        catch { return false; }
    }

    bool IsBlank(dynamic cell)
    {
        try
        {
            string t = (string?)cell.Text ?? string.Empty;
            return string.IsNullOrWhiteSpace(t);
        }
        catch { return true; }
    }

    bool IsMerged(dynamic cell)
    {
        try { return (bool)cell.MergeCells; } catch { return false; }
    }

    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        int startRow = req.StartRow <= 0 ? 1 : req.StartRow;
        int endRow = req.EndRow <= 0 ? 3 : req.EndRow;
        if (startRow != 1 || endRow != 3)
            return Results.Ok(new { ok = false, msg = "This endpoint currently supports StartRow=1 and EndRow=3 only." });

        int startCol = ExcelIO.ColumnFromA1(string.IsNullOrWhiteSpace(req.StartColumn) ? "A" : req.StartColumn);
        int endCol = ExcelIO.ColumnFromA1(string.IsNullOrWhiteSpace(req.EndColumn) ? "BQ" : req.EndColumn);
        if (endCol < startCol) { var tmp = startCol; startCol = endCol; endCol = tmp; }

        bool dryRun = req.DryRun;
        bool allowMergeTopTwo = req.AllowMergeTopTwo;

        int mergedCount = 0;
        int skippedCount = 0;
        var mergedRanges = new List<string>();

        bool prevAlerts = true;
        try { prevAlerts = (bool)excel.DisplayAlerts; } catch { }
        try { excel.DisplayAlerts = false; } catch { }

        for (int c = startCol; c <= endCol; c++)
        {
            dynamic? cell1 = null;
            dynamic? cell2 = null;
            dynamic? cell3 = null;
            dynamic? rng = null;
            try
            {
                cell1 = ws.Cells[1, c];
                cell2 = ws.Cells[2, c];
                cell3 = ws.Cells[3, c];

                bool row1Has = HasText(cell1);
                bool row2Blank = IsBlank(cell2);
                bool row3Blank = IsBlank(cell3);
                bool row2Has = HasText(cell2);

                string col = ColLetters(c);

                if (row1Has && row2Blank)
                {
                    if (row3Blank)
                    {
                        if (IsMerged(cell1) || IsMerged(cell2) || IsMerged(cell3)) { skippedCount++; continue; }
                        string addr = $"{col}1:{col}3";
                        mergedRanges.Add(addr);
                        mergedCount++;
                        if (!dryRun)
                        {
                            rng = ws.Range(addr);
                            rng.Merge();
                        }
                    }
                    else if (allowMergeTopTwo)
                    {
                        if (IsMerged(cell1) || IsMerged(cell2)) { skippedCount++; continue; }
                        string addr = $"{col}1:{col}2";
                        mergedRanges.Add(addr);
                        mergedCount++;
                        if (!dryRun)
                        {
                            rng = ws.Range(addr);
                            rng.Merge();
                        }
                    }
                }
                else if (row2Has && row3Blank)
                {
                    if (IsMerged(cell2) || IsMerged(cell3)) { skippedCount++; continue; }
                    string addr = $"{col}2:{col}3";
                    mergedRanges.Add(addr);
                    mergedCount++;
                    if (!dryRun)
                    {
                        rng = ws.Range(addr);
                        rng.Merge();
                    }
                }
            }
            catch
            {
                skippedCount++;
            }
            finally
            {
                try { if (rng != null) Marshal.ReleaseComObject(rng); } catch { }
                try { if (cell3 != null) Marshal.ReleaseComObject(cell3); } catch { }
                try { if (cell2 != null) Marshal.ReleaseComObject(cell2); } catch { }
                try { if (cell1 != null) Marshal.ReleaseComObject(cell1); } catch { }
            }
        }

        try { excel.DisplayAlerts = prevAlerts; } catch { }

        string? wbName = null;
        string? wsName = null;
        try { wbName = (string)wb.FullName; } catch { try { wbName = (string)wb.Name; } catch { } }
        try { wsName = (string)ws.Name; } catch { }

        return Results.Ok(new
        {
            ok = true,
            workbook = wbName,
            sheet = wsName,
            range = new { startColumn = req.StartColumn, endColumn = req.EndColumn, startRow, endRow },
            allowMergeTopTwo,
            dryRun,
            mergedCount,
            skippedCount,
            mergedRanges
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Activate a workbook
app.MapPost("/com/activate_workbook", (ComExcelIO.WorkbookSelector req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });
    dynamic excel = excelObj;
    dynamic? wb = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });
        wb.Activate();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex) { return Results.Ok(new { ok = false, msg = ex.Message }); }
    finally { try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { } try { Marshal.ReleaseComObject(excel); } catch { } }
});

// Activate a specific worksheet in a workbook
app.MapPost("/com/activate_sheet", (ComExcelIO.ActivateSheetRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        if (!string.IsNullOrWhiteSpace(req.SheetName))
        {
            try { ws = wb.Worksheets.Item(req.SheetName); } catch { }
            if (ws is null)
            {
                try { ws = wb.Sheets.Item(req.SheetName); } catch { }
            }
        }
        else if (req.SheetIndex.HasValue && req.SheetIndex.Value > 0)
        {
            try { ws = wb.Worksheets.Item(req.SheetIndex.Value); } catch { }
            if (ws is null)
            {
                try { ws = wb.Sheets.Item(req.SheetIndex.Value); } catch { }
            }
        }
        else
        {
            try { ws = wb.ActiveSheet; } catch { }
        }

        if (ws is null)
            return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        try { wb.Activate(); } catch { }
        ws.Activate();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Get VBA module code from an open workbook via COM
app.MapPost("/com/get_vba_module", (ComExcelIO.VbaModuleRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? vbProj = null;
    dynamic? components = null;
    dynamic? comp = null;
    dynamic? codeModule = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        try { vbProj = wb.VBProject; }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, msg = "VBProject not accessible (check macro security: Trust access to the VBA project object model). " + ex.Message });
        }

        try { components = vbProj.VBComponents; }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, msg = "VBComponents not accessible: " + ex.Message });
        }

        if (!string.IsNullOrWhiteSpace(req.ModuleName))
        {
            try { comp = components.Item(req.ModuleName); } catch { comp = null; }
        }
        if (comp is null)
        {
            try { comp = components.Item(1); } catch { comp = null; }
        }
        if (comp is null)
            return Results.Ok(new { ok = false, msg = "target VBA module not found" });

        try { codeModule = comp.CodeModule; } catch { codeModule = null; }
        if (codeModule is null)
            return Results.Ok(new { ok = false, msg = "CodeModule not available on selected component" });

        int count = 0;
        try { count = (int)codeModule.CountOfLines; } catch { count = 0; }
        string code = string.Empty;
        if (count > 0)
        {
            try { code = (string)codeModule.Lines(1, count); } catch { code = string.Empty; }
        }

        string? moduleName = null;
        try { moduleName = (string?)comp.Name; } catch { moduleName = null; }

        return Results.Ok(new { ok = true, moduleName, code });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (codeModule != null) Marshal.ReleaseComObject(codeModule); } catch { }
        try { if (comp != null) Marshal.ReleaseComObject(comp); } catch { }
        try { if (components != null) Marshal.ReleaseComObject(components); } catch { }
        try { if (vbProj != null) Marshal.ReleaseComObject(vbProj); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Overwrite VBA module code in an open workbook via COM
app.MapPost("/com/set_vba_module", (ComExcelIO.SetVbaModuleRequest req) =>
{
    if (req.Code is null)
        return Results.BadRequest(new { ok = false, msg = "code is required" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? vbProj = null;
    dynamic? components = null;
    dynamic? comp = null;
    dynamic? codeModule = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });

        try { vbProj = wb.VBProject; }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, msg = "VBProject not accessible (check macro security: Trust access to the VBA project object model). " + ex.Message });
        }

        try { components = vbProj.VBComponents; }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, msg = "VBComponents not accessible: " + ex.Message });
        }

        if (!string.IsNullOrWhiteSpace(req.ModuleName))
        {
            try { comp = components.Item(req.ModuleName); } catch { comp = null; }
        }
        if (comp is null)
        {
            try { comp = components.Item(1); } catch { comp = null; }
        }
        if (comp is null)
            return Results.Ok(new { ok = false, msg = "target VBA module not found" });

        try { codeModule = comp.CodeModule; } catch { codeModule = null; }
        if (codeModule is null)
            return Results.Ok(new { ok = false, msg = "CodeModule not available on selected component" });

        int count = 0;
        try { count = (int)codeModule.CountOfLines; } catch { count = 0; }
        if (count > 0)
        {
            try { codeModule.DeleteLines(1, count); } catch { }
        }

        if (!string.IsNullOrEmpty(req.Code))
        {
            try { codeModule.AddFromString(req.Code); } catch (Exception ex) { return Results.Ok(new { ok = false, msg = "failed to add code: " + ex.Message }); }
        }

        string? moduleName = null;
        try { moduleName = (string?)comp.Name; } catch { moduleName = null; }

        return Results.Ok(new { ok = true, moduleName });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
    finally
    {
        try { if (codeModule != null) Marshal.ReleaseComObject(codeModule); } catch { }
        try { if (comp != null) Marshal.ReleaseComObject(comp); } catch { }
        try { if (components != null) Marshal.ReleaseComObject(components); } catch { }
        try { if (vbProj != null) Marshal.ReleaseComObject(vbProj); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Add a worksheet
app.MapPost("/com/add_sheet", (ComExcelIO.AddSheetRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });
    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? wsNew = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });
        wsNew = wb.Worksheets.Add();
        if (!string.IsNullOrWhiteSpace(req.NewSheetName))
            wsNew.Name = req.NewSheetName;
        object missing = Type.Missing;
        if (!string.IsNullOrWhiteSpace(req.AfterSheetName))
        {
            dynamic after = null;
            try { after = wb.Worksheets.Item(req.AfterSheetName); wsNew.Move(missing, after); }
            finally { try { if (after != null) Marshal.ReleaseComObject(after); } catch { } }
        }
        else if (!string.IsNullOrWhiteSpace(req.BeforeSheetName))
        {
            dynamic before = null;
            try { before = wb.Worksheets.Item(req.BeforeSheetName); wsNew.Move(before, missing); }
            finally { try { if (before != null) Marshal.ReleaseComObject(before); } catch { } }
        }
        return Results.Ok(new { ok = true, name = (string?)wsNew.Name });
    }
    catch (Exception ex) { return Results.Ok(new { ok = false, msg = ex.Message }); }
    finally { try { if (wsNew != null) Marshal.ReleaseComObject(wsNew); } catch { } try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { } try { Marshal.ReleaseComObject(excel); } catch { } }
});

// Delete a worksheet
app.MapPost("/com/delete_sheet", (ComExcelIO.DeleteSheetRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.SheetName))
        return Results.BadRequest(new { ok = false, msg = "sheetName is required" });
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });
    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null)
            return Results.Ok(new { ok = false, msg = "target workbook not found" });
        bool origAlerts = true;
        try { origAlerts = (bool)excel.DisplayAlerts; } catch { }
        try
        {
            excel.DisplayAlerts = false;
            ws = wb.Worksheets.Item(req.SheetName);
            ws.Delete();
        }
        finally
        {
            try { excel.DisplayAlerts = origAlerts; } catch { }
        }
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex) { return Results.Ok(new { ok = false, msg = ex.Message }); }
    finally { try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { } try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { } try { Marshal.ReleaseComObject(excel); } catch { } }
});

// Sort a range by keys
app.MapPost("/com/sort_range", (ComExcelIO.SortRangeRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.RangeA1) || req.Keys is null || req.Keys.Count == 0)
        return Results.BadRequest(new { ok = false, msg = "rangeA1 and keys are required" });

    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    dynamic? rng = null;
    dynamic? sort = null;
    var keyRanges = new List<object>();
    try
    {
        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName)
             ?? excel.ActiveWorkbook;
        if (wb is null) return Results.Ok(new { ok = false, msg = "target workbook not found" });
        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.Ok(new { ok = false, msg = "target worksheet not found" });
        rng = ws.Range(req.RangeA1);

        try
        {
            sort = ws.Sort;
            sort.SortFields.Clear();

            foreach (var key in req.Keys)
            {
                int colIndex = 0;
                if (key.ColumnIndex.HasValue) colIndex = key.ColumnIndex.Value;
                else if (!string.IsNullOrWhiteSpace(key.ColumnKey))
                {
                    try { colIndex = ExcelIO.ColumnFromA1(key.ColumnKey!) - ExcelIO.ColumnFromA1("A") + 1; } catch { colIndex = 1; }
                }
                if (colIndex <= 0) colIndex = 1;
                dynamic colRange = null;
                try
                {
                    colRange = rng.Columns[colIndex];
                    int order = (string.Equals(key.Order, "desc", StringComparison.OrdinalIgnoreCase) ? 2 : 1); // xlDescending=2, xlAscending=1
                    sort.SortFields.Add(colRange, 0, order, Type.Missing, 1);
                    keyRanges.Add(colRange);
                }
                catch { try { if (colRange != null) Marshal.ReleaseComObject(colRange); } catch { } }
            }

            sort.SetRange(rng);
            sort.Header = (req.HasHeader == true) ? 1 : 2; // xlYes=1, xlNo=2
            sort.MatchCase = false;
            sort.Orientation = 1; // xlTopToBottom
            sort.Apply();

            return Results.Ok(new { ok = true, engine = "excel" });
        }
        catch
        {
            // Fallback: in-memory sort
            object? v = null;
            try { v = rng.Value2; } catch { try { v = rng.Value; } catch { } }
            var rows = ComUtil.ToJagged(v);
            if (rows.Count == 0) return Results.Ok(new { ok = true, engine = "fallback", note = "no data" });
            int startIndex = (req.HasHeader == true && rows.Count > 1) ? 1 : 0;
            var data = rows.Skip(startIndex).ToList();

            // Compose comparison based on keys
            Comparison<List<object?>> cmp = (a, b) =>
            {
                foreach (var key in req.Keys)
                {
                    int colIndex = 0;
                    if (key.ColumnIndex.HasValue) colIndex = key.ColumnIndex.Value;
                    else if (!string.IsNullOrWhiteSpace(key.ColumnKey))
                    {
                        try { colIndex = ExcelIO.ColumnFromA1(key.ColumnKey!) - ExcelIO.ColumnFromA1("A") + 1; } catch { colIndex = 1; }
                    }
                    if (colIndex <= 0) colIndex = 1;
                    int c = ComUtil.CompareCell(a, b, colIndex - 1);
                    if (c != 0)
                    {
                        bool desc = string.Equals(key.Order, "desc", StringComparison.OrdinalIgnoreCase);
                        return desc ? -c : c;
                    }
                }
                return 0;
            };
            data.Sort(cmp);
            var sorted = new List<List<object?>>();
            if (startIndex == 1) sorted.Add(rows[0]);
            sorted.AddRange(data);
            int rcount = sorted.Count;
            int ccount = sorted.Max(r => r?.Count ?? 0);
            object[,] box = ComUtil.To2DArray(sorted, rcount, ccount);
            try { rng.Value2 = box; } catch { rng.Value = box; }
            return Results.Ok(new { ok = true, engine = "fallback" });
        }
    }
    catch (Exception ex) { return Results.Ok(new { ok = false, msg = ex.Message }); }
    finally
    {
        foreach (var obj in keyRanges)
        {
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
        try { if (sort != null) Marshal.ReleaseComObject(sort); } catch { }
        try { if (rng != null) Marshal.ReleaseComObject(rng); } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Delete columns that have a header but no data below it
app.MapPost("/com/delete_empty_columns", (ComExcelIO.DeleteEmptyColumnsRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    var deleted = new List<object>();
    try
    {
        bool origScreenUpdating = true;
        bool origEnableEvents = true;
        int origCalc = -4105; // xlCalculationAutomatic
        try { origScreenUpdating = (bool)excel.ScreenUpdating; } catch { }
        try { origEnableEvents = (bool)excel.EnableEvents; } catch { }
        try { origCalc = (int)excel.Calculation; } catch { }
        try { excel.ScreenUpdating = false; } catch { }
        try { excel.EnableEvents = false; } catch { }
        try { excel.Calculation = -4135; } catch { } // xlCalculationManual

        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName) ?? excel.ActiveWorkbook;
        if (wb is null) return Results.Ok(new { ok = false, msg = "target workbook not found" });
        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        int headerRow = req.HeaderRow <= 0 ? 1 : req.HeaderRow;
        int dataStartRow = req.DataStartRow.HasValue && req.DataStartRow.Value > 0 ? req.DataStartRow.Value : headerRow + 1;
        var protect = new HashSet<string>(req.ProtectHeaders ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        // Determine last header column from header row only (cap by scan limit)
        int lastHeaderCol = 0;
        try { lastHeaderCol = (int)ws.Cells[headerRow, ws.Columns.Count].End(-4159).Column; } catch { lastHeaderCol = 0; } // xlToLeft
        int headerScanLimit = req.MaxHeaderScanCols.HasValue && req.MaxHeaderScanCols.Value > 0 ? req.MaxHeaderScanCols.Value : 400;
        if (lastHeaderCol == 0 || lastHeaderCol > headerScanLimit) lastHeaderCol = headerScanLimit;

        // Determine last data row by Column A (col=1) as baseline, optionally cap by MaxDataRows
        int lastDataRow = dataStartRow;
        try { lastDataRow = (int)ws.Cells[ws.Rows.Count, 1].End(-4162).Row; } catch { lastDataRow = dataStartRow; } // xlUp
        if (req.MaxDataRows.HasValue && req.MaxDataRows.Value > 0)
        {
            int cap = headerRow + req.MaxDataRows.Value;
            if (lastDataRow > cap) lastDataRow = cap;
        }
        if (lastDataRow < dataStartRow) lastDataRow = dataStartRow;

        if (lastHeaderCol <= 0)
            return Results.Ok(new { ok = true, deleted = deleted, deletedCount = 0 });

        // Build candidate columns (non-empty header), evaluate right-to-left
        var cand = new List<(int col, string header)>();
        for (int c = 1; c <= lastHeaderCol; c++)
        {
            string? header = null;
            try { header = ws.Cells[headerRow, c].Value2?.ToString(); } catch { header = null; }
            if (string.IsNullOrWhiteSpace(header)) continue;
            if (protect.Contains(header!)) continue;
            cand.Add((c, header!));
        }
        if (cand.Count == 0)
            return Results.Ok(new { ok = true, deleted = deleted, deletedCount = 0, headerRow, dataStartRow, lastDataRow, lastHeaderCol });

        // Pull a single big block to minimize COM round-trips
        object? blockVals = null;
        try
        {
            dynamic b1 = ws.Cells[dataStartRow, 1];
            dynamic b2 = ws.Cells[lastDataRow, lastHeaderCol];
            dynamic block = ws.Range(b1, b2);
            try { blockVals = block.Value2; }
            finally
            {
                try { Marshal.ReleaseComObject(block); } catch { }
                try { Marshal.ReleaseComObject(b2); } catch { }
                try { Marshal.ReleaseComObject(b1); } catch { }
            }
        }
        catch { blockVals = null; }

        var rows = ComUtil.ToJagged(blockVals); // size: (lastDataRow - dataStartRow + 1) x lastHeaderCol
        var delIndices = new List<(int col, string header)>();
        foreach (var (c, header) in cand)
        {
            bool hasData = false;
            int relCol = c - 1; // since block starts at col 1
            for (int r = 0; r < rows.Count; r++)
            {
                var line = rows[r];
                object? v = (relCol < line.Count) ? line[relCol] : null;
                if (v != null && !(v is string s && string.IsNullOrWhiteSpace(s)))
                {
                    hasData = true;
                    break;
                }
            }
            if (!hasData) delIndices.Add((c, header));
        }

        // Delete from right to left
        for (int i = delIndices.Count - 1; i >= 0; i--)
        {
            var (c, header) = delIndices[i];
            try { ws.Columns[c].Delete(); deleted.Add(new { column = c, header }); }
            catch { }
        }

        return Results.Ok(new { ok = true, deletedCount = deleted.Count, deleted, headerRow, dataStartRow, lastDataRow, lastHeaderCol });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message, deletedCount = deleted.Count, deleted });
    }
    finally
    {
        try { excel.Calculation = -4105; } catch { }
        try { excel.EnableEvents = true; } catch { }
        try { excel.ScreenUpdating = true; } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Delete columns by 1-based indices (right-to-left)
app.MapPost("/com/delete_columns_by_index", (ComExcelIO.DeleteColumnsByIndexRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });
    if (req.Columns == null || req.Columns.Count == 0)
        return Results.BadRequest(new { ok = false, msg = "columns is required" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    int deleted = 0;
    try
    {
        bool origScreenUpdating = true;
        bool origEnableEvents = true;
        int origCalc = -4105;
        try { origScreenUpdating = (bool)excel.ScreenUpdating; } catch { }
        try { origEnableEvents = (bool)excel.EnableEvents; } catch { }
        try { origCalc = (int)excel.Calculation; } catch { }
        try { excel.ScreenUpdating = false; } catch { }
        try { excel.EnableEvents = false; } catch { }
        try { excel.Calculation = -4135; } catch { }

        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName) ?? excel.ActiveWorkbook;
        if (wb is null) return Results.Ok(new { ok = false, msg = "target workbook not found" });
        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        var cols = req.Columns.Where(c => c > 0).Distinct().OrderByDescending(c => c).ToList();
        foreach (var c in cols)
        {
            try { ws.Columns[c].Delete(); deleted++; }
            catch { }
        }
        return Results.Ok(new { ok = true, deleted });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message, deleted });
    }
    finally
    {
        try { excel.Calculation = -4105; } catch { }
        try { excel.EnableEvents = true; } catch { }
        try { excel.ScreenUpdating = true; } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Resize columns/rows by indices (COM)
app.MapPost("/com/resize", (ComExcelIO.ResizeRequest req) =>
{
    object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
    if (excelObj is null)
        return Results.Ok(new { ok = false, msg = "Excel is not running" });

    if ((req.ColumnIndices is null || req.ColumnIndices.Count == 0) &&
        (req.RowIndices is null || req.RowIndices.Count == 0))
        return Results.BadRequest(new { ok = false, msg = "Specify ColumnIndices and/or RowIndices" });

    dynamic excel = excelObj;
    dynamic? wb = null;
    dynamic? ws = null;
    int colsChanged = 0, rowsChanged = 0;
    try
    {
        bool origScreenUpdating = true;
        bool origEnableEvents = true;
        int origCalc = -4105;
        try { origScreenUpdating = (bool)excel.ScreenUpdating; } catch { }
        try { origEnableEvents = (bool)excel.EnableEvents; } catch { }
        try { origCalc = (int)excel.Calculation; } catch { }
        try { excel.ScreenUpdating = false; } catch { }
        try { excel.EnableEvents = false; } catch { }
        try { excel.Calculation = -4135; } catch { }

        wb = ComUtil.FindWorkbook(excel, req.WorkbookFullName, req.WorkbookName) ?? excel.ActiveWorkbook;
        if (wb is null) return Results.Ok(new { ok = false, msg = "target workbook not found" });
        ws = ComUtil.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.Ok(new { ok = false, msg = "target worksheet not found" });

        // Columns
        if (req.ColumnIndices is not null && req.ColumnIndices.Count > 0)
        {
            var cols = req.ColumnIndices.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
            foreach (var c in cols)
            {
                try
                {
                    dynamic col = ws.Columns[c];
                    try
                    {
                        if (req.AutoFitColumns == true)
                            col.AutoFit();
                        else if (req.ColumnWidth.HasValue)
                            col.ColumnWidth = req.ColumnWidth.Value;
                        else
                            col.AutoFit();
                        colsChanged++;
                    }
                    finally { try { Marshal.ReleaseComObject(col); } catch { } }
                }
                catch { }
            }
        }

        // Rows
        if (req.RowIndices is not null && req.RowIndices.Count > 0)
        {
            var rows = req.RowIndices.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
            foreach (var r in rows)
            {
                try
                {
                    dynamic row = ws.Rows[r];
                    try
                    {
                        if (req.AutoFitRows == true)
                            row.AutoFit();
                        else if (req.RowHeight.HasValue)
                            row.RowHeight = req.RowHeight.Value;
                        else
                            row.AutoFit();
                        rowsChanged++;
                    }
                    finally { try { Marshal.ReleaseComObject(row); } catch { } }
                }
                catch { }
            }
        }

        return Results.Ok(new { ok = true, colsChanged, rowsChanged });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message, colsChanged, rowsChanged });
    }
    finally
    {
        try { excel.Calculation = -4105; } catch { }
        try { excel.EnableEvents = true; } catch { }
        try { excel.ScreenUpdating = true; } catch { }
        try { if (ws != null) Marshal.ReleaseComObject(ws); } catch { }
        try { if (wb != null) Marshal.ReleaseComObject(wb); } catch { }
        try { Marshal.ReleaseComObject(excel); } catch { }
    }
});

// Align headers in a file (ClosedXML, not COM). Writes a new file.
app.MapPost("/file/align_headers", (FileAlign.AlignHeadersRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (req.Headers is null || req.Headers.Length == 0)
        return Results.BadRequest(new { ok = false, msg = "headers is required" });
    try
    {
        string output = string.IsNullOrWhiteSpace(req.OutputPath)
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(req.ExcelPath) ?? string.Empty,
                                      System.IO.Path.GetFileNameWithoutExtension(req.ExcelPath) + "R" + System.IO.Path.GetExtension(req.ExcelPath))
            : req.OutputPath!;

        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = wb.Worksheets.FirstOrDefault() ?? wb.AddWorksheet("Sheet1");

        int headerRow = req.HeaderRow <= 0 ? 1 : req.HeaderRow;
        int firstDataRow = headerRow + 1;

        // Build header index map from source
        var srcHeaders = new List<string>();
        int scanCols = Math.Max(1, req.ScanHeaderCols <= 0 ? 120 : req.ScanHeaderCols);
        for (int c = 1; c <= scanCols; c++)
        {
            var txt = ws.Cell(headerRow, c).GetString().Trim();
            srcHeaders.Add(txt);
        }
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < srcHeaders.Count; i++)
        {
            var h = srcHeaders[i];
            if (!string.IsNullOrWhiteSpace(h) && !map.ContainsKey(h)) map[h] = i + 1; // 1-based
        }
        map.TryGetValue("paramName", out int paramNameCol);

        // Determine last data row from column A (or specified key column)
        int keyCol = req.KeyColumn <= 0 ? 1 : req.KeyColumn;
        int lastRow = firstDataRow;
        try
        {
            var last = ws.Cell(ws.Rows().Count(), keyCol).Address.RowNumber;
            var up = ws.Cell(ws.Rows().Count(), keyCol).WorksheetColumn().ColumnNumber();
            // fallback using ClosedXML scanning
        }
        catch { }
        // Fallback scan by checking a few hundred rows
        int maxRows = req.MaxDataRows <= 0 ? 500 : req.MaxDataRows;
        lastRow = Math.Max(firstDataRow, ws.Range(firstDataRow, keyCol, firstDataRow + maxRows - 1, keyCol)
                                  .Cells().LastOrDefault(c => !string.IsNullOrWhiteSpace(c.GetString()))?.Address.RowNumber ?? firstDataRow);

        // Create a new workbook to write aligned data
        using var outWb = new XLWorkbook();
        var outWs = outWb.AddWorksheet("Aligned");

        // Write headers
        for (int c = 0; c < req.Headers.Length; c++)
        {
            outWs.Cell(headerRow, c + 1).Value = req.Headers[c];
        }

        // Copy/transform data rows
        for (int r = firstDataRow; r <= lastRow; r++)
        {
            for (int c = 0; c < req.Headers.Length; c++)
            {
                string h = req.Headers[c];
                object? val = null;
                if (map.TryGetValue(h, out int srcCol))
                {
                    val = ws.Cell(r, srcCol).Value;
                }
                else if (req.TreatParamNameAsName && string.Equals(h, "name", StringComparison.OrdinalIgnoreCase) && paramNameCol > 0)
                {
                    val = ws.Cell(r, paramNameCol).Value;
                }
                // else leave null (empty)
                // Assign with type handling for ClosedXML
                var cell = outWs.Cell(r, c + 1);
                if (val is null)
                    cell.Clear();
                else if (val is string s)
                    cell.Value = s;
                else if (val is bool b)
                    cell.Value = b;
                else if (val is DateTime dt)
                    cell.Value = dt;
                else if (val is double d)
                    cell.Value = d;
                else if (val is float f)
                    cell.Value = (double)f;
                else if (val is int ii)
                    cell.Value = (double)ii;
                else if (val is long ll)
                    cell.Value = (double)ll;
                else if (val is decimal mm)
                    cell.Value = (double)mm;
                else
                    cell.Value = val.ToString();
            }
        }

        outWb.SaveAs(output);
        return Results.Ok(new { ok = true, output });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

 

app.MapPost("/parse_plan", (ExcelParser.ParseRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    var sheetName = string.IsNullOrWhiteSpace(req.SheetName) ? "Sheet1" : req.SheetName;
    double cellSizeMeters = req.CellSizeMeters <= 0 ? 1.0 : req.CellSizeMeters;
    bool useColorMask = req.UseColorMask ?? false;

    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                 ?? wb.Worksheets.FirstOrDefault();
        if (ws == null)
            return Results.BadRequest(new { ok = false, msg = $"sheet not found: {sheetName}" });

        // New: Determine scan region prioritizing colored cells, then fallback to A1-origin broad scan (ignoring UsedRange)
        int SCAN_MAX_ROWS = 200;
        int SCAN_MAX_COLS = 200;
        int firstRow = 1, firstCol = 1, lastRow = SCAN_MAX_ROWS, lastCol = SCAN_MAX_COLS;

        // 1) Prefer colored region (even if useColorMask=false)
        bool foundColor = false;
        int cfr = int.MaxValue, cfc = int.MaxValue, clr = int.MinValue, clc = int.MinValue;
        for (int r = 1; r <= SCAN_MAX_ROWS; r++)
        {
            for (int c = 1; c <= SCAN_MAX_COLS; c++)
            {
                var fill = ws.Cell(r, c).Style.Fill;
                bool colored = false;
                try
                {
                    if (fill.PatternType != XLFillPatternValues.None) colored = true;
                    var txt = fill.BackgroundColor?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(txt) && !txt.Contains("White", StringComparison.OrdinalIgnoreCase) && !txt.Contains("NoColor", StringComparison.OrdinalIgnoreCase))
                        colored = true;
                }
                catch { }
                if (colored)
                {
                    foundColor = true;
                    cfr = Math.Min(cfr, r);
                    cfc = Math.Min(cfc, c);
                    clr = Math.Max(clr, r);
                    clc = Math.Max(clc, c);
                }
            }
        }
        if (foundColor)
        {
            firstRow = cfr; firstCol = cfc; lastRow = clr; lastCol = clc;
        }
        // else keep default A1..SCAN_MAX

        int width = Math.Max(0, lastCol - firstCol + 1);
        int height = Math.Max(0, lastRow - firstRow + 1);
        var unitEdges = new List<ExcelParser.UnitEdge>();
        var labels = new List<ExcelParser.LabelOut>();
        int? firstBorderRow = null; // top-left (for diagnostics)
        int? firstBorderCol = null;
        int? originBorderRow = null; // bottom-left (origin)
        int? originBorderCol = null;
        string ToA1(int r, int c)
        {
            string ColLetters(int col)
            {
                var s = string.Empty;
                while (col > 0)
                {
                    int rem = (col - 1) % 26;
                    s = (char)('A' + rem) + s;
                    col = (col - 1) / 26;
                }
                return s;
            }
            return $"{ColLetters(c)}{r}";
        }

        for (int r = firstRow; r <= lastRow; r++)
        {
            for (int c = firstCol; c <= lastCol; c++)
            {
                var cell = ws.Cell(r, c);
                int cx = c - firstCol;
                int cy = r - firstRow;
                var b = cell.Style.Border;

                void AddEdgeIf(XLBorderStyleValues style, int x1, int y1, int x2, int y2)
                {
                    if (style == XLBorderStyleValues.None) return;
                    unitEdges.Add(new ExcelParser.UnitEdge { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StyleKey = ExcelParser.StyleKey(style) });
                }

                bool hasBorder = false;
                if (b.TopBorder != XLBorderStyleValues.None) { AddEdgeIf(b.TopBorder, cx, cy, cx + 1, cy); hasBorder = true; }
                if (b.BottomBorder != XLBorderStyleValues.None) { AddEdgeIf(b.BottomBorder, cx, cy + 1, cx + 1, cy + 1); hasBorder = true; }
                if (b.LeftBorder != XLBorderStyleValues.None) { AddEdgeIf(b.LeftBorder, cx, cy, cx, cy + 1); hasBorder = true; }
                if (b.RightBorder != XLBorderStyleValues.None) { AddEdgeIf(b.RightBorder, cx + 1, cy, cx + 1, cy + 1); hasBorder = true; }

                if (hasBorder)
                {
                    int absRow = r; // 1-based
                    int absCol = c;
                    // track top-left for reference
                    if (!firstBorderRow.HasValue || absRow < firstBorderRow.Value || (absRow == firstBorderRow.Value && absCol < firstBorderCol))
                    { firstBorderRow = absRow; firstBorderCol = absCol; }
                    // track bottom-left for origin (largest row, then smallest col)
                    if (!originBorderRow.HasValue || absRow > originBorderRow.Value || (absRow == originBorderRow.Value && absCol < originBorderCol))
                    { originBorderRow = absRow; originBorderCol = absCol; }
                }

                var txt = (cell.GetFormattedString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    labels.Add(new ExcelParser.LabelOut { Text = txt, X = (cx + 0.5) * cellSizeMeters, Y = (height - (cy + 0.5)) * cellSizeMeters });
                }
            }
        }

        var segs = ExcelParser.CollapseToMaxSegmentsWithStyle(unitEdges);
        var detailed = new List<object>();
        foreach (var s in segs)
        {
            double x1m = s.X1 * cellSizeMeters;
            double y1m = (height - s.Y1) * cellSizeMeters;
            double x2m = s.X2 * cellSizeMeters;
            double y2m = (height - s.Y2) * cellSizeMeters;
            string kind = Math.Abs(y1m - y2m) < 1e-9 ? "H" : (Math.Abs(x1m - x2m) < 1e-9 ? "V" : "Other");
            double len = Math.Sqrt((x2m - x1m) * (x2m - x1m) + (y2m - y1m) * (y2m - y1m));
            detailed.Add(new { kind, x1 = x1m, y1 = y1m, x2 = x2m, y2 = y2m, length_m = len, style = s.StyleKey });
        }

        return Results.Ok(new
        {
            ok = true,
            widthCells = width,
            heightCells = height,
            cellSizeMeters,
            segmentsDetailed = detailed,
            labels,
            scanRegion = new { firstRow, firstCol, lastRow, lastCol },
            firstBorderCell = (firstBorderRow.HasValue && firstBorderCol.HasValue)
                ? new { row = firstBorderRow.Value, col = firstBorderCol.Value, address = ToA1(firstBorderRow.Value, firstBorderCol.Value) }
                : null,
            originBorderCell = (originBorderRow.HasValue && originBorderCol.HasValue)
                ? new { row = originBorderRow.Value, col = originBorderCol.Value, address = ToA1(originBorderRow.Value, originBorderCol.Value) }
                : null
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// List worksheets and basic info
app.MapPost("/sheet_info", (ExcelIO.SheetInfoRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var sheets = wb.Worksheets.Select(ws => new
        {
            name = ws.Name,
            usedRange = ws.RangeUsed()?.RangeAddress.ToStringRelative(true) ?? null,
            firstCell = ws.FirstCellUsed()?.Address.ToString() ?? null,
            lastCell = ws.LastCellUsed()?.Address.ToString() ?? null,
            rowCount = ws.RangeUsed()?.RowCount() ?? 0,
            columnCount = ws.RangeUsed()?.ColumnCount() ?? 0
        }).ToList();
        return Results.Ok(new { ok = true, sheets });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Read a rectangular range
app.MapPost("/read_cells", (ExcelIO.ReadCellsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.BadRequest(new { ok = false, msg = $"sheet not found: {req.SheetName}" });

        var range = ExcelIO.ResolveRange(ws, req.RangeA1);
        if (range is null)
        {
            var used = ws.RangeUsed();
            if (used is null) return Results.Ok(new { ok = true, cells = Array.Empty<object>() });
            range = used;
        }

        var cells = new List<object>();
        foreach (var cell in range.CellsUsed(c => true))
        {
            object? raw = null;
            if (req.ReturnRaw)
            {
                try { raw = cell.Value.ToString(); } catch { raw = null; }
            }
            cells.Add(new
            {
                address = cell.Address.ToString(),
                row = cell.Address.RowNumber,
                column = cell.Address.ColumnNumber,
                value = raw,
                text = req.ReturnFormatted ? cell.GetFormattedString() : null,
                formula = req.IncludeFormula ? (string.IsNullOrWhiteSpace(cell.FormulaA1) ? null : cell.FormulaA1) : null,
                dataType = cell.DataType.ToString()
            });
        }
        return Results.Ok(new { ok = true, cells });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Write a 2D block of values starting at a given cell
app.MapPost("/write_cells", (ExcelIO.WriteCellsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (string.IsNullOrWhiteSpace(req.StartCell))
        return Results.BadRequest(new { ok = false, msg = "startCell is required (e.g. 'B2')" });
    if (req.Values == null || req.Values.Length == 0)
        return Results.BadRequest(new { ok = false, msg = "values must be a 2D array with at least 1 row" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveOrCreateWorksheet(wb, req.SheetName);

        var start = ws.Cell(req.StartCell);
        int rows = req.Values.Length;
        int cols = req.Values.Max(r => r?.Length ?? 0);
        for (int r = 0; r < rows; r++)
        {
            var row = req.Values[r] ?? Array.Empty<object?>();
            for (int c = 0; c < cols; c++)
            {
                var cell = start.Worksheet.Cell(start.Address.RowNumber + r, start.Address.ColumnNumber + c);
                if (c < row.Length)
                {
                    var v = row[c];
                    if (v is null)
                    {
                        if (req.TreatNullAsClear) cell.Clear();
                    }
                    else
                    {
                        ExcelIO.ApplyValue(cell, v);
                    }
                }
                else if (req.TreatNullAsClear)
                {
                    cell.Clear();
                }
            }
        }
        wb.Save();
        return Results.Ok(new { ok = true, wroteRows = rows, wroteCols = cols });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Append rows under last used row
app.MapPost("/append_rows", (ExcelIO.AppendRowsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (req.Rows == null || req.Rows.Length == 0)
        return Results.BadRequest(new { ok = false, msg = "rows must be provided" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveOrCreateWorksheet(wb, req.SheetName);
        int startRow = Math.Max(1, ws.LastRowUsed()?.RowNumber() + 1 ?? 1);
        int startCol = string.IsNullOrWhiteSpace(req.StartColumn) ? 1 : ExcelIO.ColumnFromA1(req.StartColumn);
        int wrote = 0;
        foreach (var row in req.Rows)
        {
            if (row == null) continue;
            for (int c = 0; c < row.Length; c++)
            {
                ExcelIO.ApplyValue(ws.Cell(startRow, startCol + c), row[c]);
            }
            startRow++;
            wrote++;
        }
        wb.Save();
        return Results.Ok(new { ok = true, appendedRows = wrote });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Set formulas to a single cell or a rectangular range (fills same formula or R1C1 not supported here)
app.MapPost("/set_formula", (ExcelIO.SetFormulaRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (string.IsNullOrWhiteSpace(req.Target))
        return Results.BadRequest(new { ok = false, msg = "target is required (A1 or A1:B3)" });
    if (string.IsNullOrWhiteSpace(req.FormulaA1))
        return Results.BadRequest(new { ok = false, msg = "formulaA1 is required (e.g. '=SUM(A1:A10)')" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveOrCreateWorksheet(wb, req.SheetName);
        var range = ExcelIO.ResolveRange(ws, req.Target) ?? ws.Range(req.Target);
        foreach (var cell in range.Cells())
        {
            cell.FormulaA1 = req.FormulaA1;
        }
        wb.Save();
        var count = range.RowCount() * range.ColumnCount();
        return Results.Ok(new { ok = true, cells = count });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Column width / row height / autofit
app.MapPost("/format_sheet", (ExcelIO.FormatSheetRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveOrCreateWorksheet(wb, req.SheetName);

        if (req.AutoFitColumns == true)
        {
            if (string.IsNullOrWhiteSpace(req.Columns)) ws.Columns().AdjustToContents();
            else ws.Columns(req.Columns).AdjustToContents();
        }
        if (req.AutoFitRows == true)
        {
            if (string.IsNullOrWhiteSpace(req.Rows)) ws.Rows().AdjustToContents();
            else ws.Rows(req.Rows).AdjustToContents();
        }
        if (req.ColumnWidths != null)
        {
            foreach (var kv in req.ColumnWidths)
            {
                int col = ExcelIO.ColumnFromA1(kv.Key);
                double width = req.WidthUnit == "pixels" ? ExcelIO.ColumnWidthFromPixels(kv.Value) : kv.Value;
                ws.Column(col).Width = width;
            }
        }
        if (req.RowHeights != null)
        {
            foreach (var kv in req.RowHeights)
            {
                if (int.TryParse(kv.Key, out var rowNum))
                {
                    ws.Row(rowNum).Height = kv.Value;
                }
            }
        }

        wb.Save();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Fill background color for a range (file-based; uses ClosedXML)
app.MapPost("/format_range_fill", (ExcelIO.FormatRangeFillRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (string.IsNullOrWhiteSpace(req.Target))
        return Results.BadRequest(new { ok = false, msg = "target is required (A1 range)" });

    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveOrCreateWorksheet(wb, req.SheetName);
        var range = ExcelIO.ResolveRange(ws, req.Target);
        if (range is null)
            return Results.Ok(new { ok = false, msg = "target range not found" });

        // Currently we only need white fill; this can be extended later if necessary.
        range.Style.Fill.BackgroundColor = XLColor.White;

        wb.Save();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Clear background (A:J) where F column <= threshold (file-based)
app.MapPost("/clear_bg_by_f_threshold", (ExcelIO.ClearBackgroundByFThresholdRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });

    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveWorksheet(wb, req.SheetName);
        if (ws is null)
            return Results.BadRequest(new { ok = false, msg = $"sheet not found: {req.SheetName}" });

        var used = ws.RangeUsed();
        if (used is null)
            return Results.Ok(new { ok = true, updated = 0, lastRow = 0 });

        int lastRow = used.RangeAddress.LastAddress.RowNumber;
        int headerRow = req.HeaderRow <= 0 ? 1 : req.HeaderRow;
        int firstDataRow = headerRow + 1;
        int fCol = ExcelIO.ColumnFromA1(req.FColumn);
        int updated = 0;

        for (int r = firstDataRow; r <= lastRow; r++)
        {
            var fCell = ws.Cell(r, fCol);
            if (fCell.IsEmpty())
                continue;

            double num;
            var text = fCell.GetValue<string>();
            if (!double.TryParse(text, out num))
                continue;

            if (num <= req.Threshold)
            {
                var rowRange = ws.Range(r, 1, r, 10); // A(1)..J(10)
                rowRange.Style.Fill.BackgroundColor = XLColor.White;
                updated++;
            }
        }

        wb.Save();
        return Results.Ok(new { ok = true, updated, lastRow });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Export sheet to CSV
app.MapPost("/to_csv", (ExcelIO.ToCsvRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.BadRequest(new { ok = false, msg = $"sheet not found: {req.SheetName}" });
        var used = ws.RangeUsed();
        if (used is null)
        {
            System.IO.File.WriteAllText(req.OutputCsvPath, string.Empty);
            return Results.Ok(new { ok = true, rows = 0, cols = 0, path = req.OutputCsvPath });
        }

        var delim = string.IsNullOrEmpty(req.Delimiter) ? "," : req.Delimiter;
        var lines = new List<string>();
        int rcount = used.RowCount();
        int ccount = used.ColumnCount();
        for (int r = 0; r < rcount; r++)
        {
            var cells = new List<string>();
            for (int c = 0; c < ccount; c++)
            {
                var cell = used.Cell(r + 1, c + 1);
                var txt = req.UseFormattedText ? cell.GetFormattedString() : (cell.Value.ToString() ?? string.Empty);
                cells.Add(ExcelIO.CsvEscape(txt, delim, req.Quote ?? '"'));
            }
            lines.Add(string.Join(delim, cells));
        }
        var enc = ExcelIO.ResolveEncoding(req.EncodingName) ?? System.Text.Encoding.UTF8;
        System.IO.File.WriteAllLines(req.OutputCsvPath, lines, enc);
        return Results.Ok(new { ok = true, rows = rcount, cols = ccount, path = req.OutputCsvPath });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Export sheet to JSON
app.MapPost("/to_json", (ExcelIO.ToJsonRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    if (string.IsNullOrWhiteSpace(req.OutputJsonPath))
        return Results.BadRequest(new { ok = false, msg = "outputJsonPath is required" });

    try
    {
        using var wb = new XLWorkbook(req.ExcelPath);
        var ws = ExcelIO.ResolveWorksheet(wb, req.SheetName);
        if (ws is null) return Results.BadRequest(new { ok = false, msg = $"sheet not found: {req.SheetName}" });

        var range = ExcelIO.ResolveRange(ws, req.RangeA1) ?? ws.RangeUsed();
        if (range is null)
        {
            ExcelIO.EnsureDirectoryForFile(req.OutputJsonPath);
            System.IO.File.WriteAllText(req.OutputJsonPath, "[]", new System.Text.UTF8Encoding(false));
            return Results.Ok(new { ok = true, mode = req.Mode, rows = 0, cols = 0, path = req.OutputJsonPath });
        }

        var mode = string.Equals(req.Mode, "matrix", StringComparison.OrdinalIgnoreCase) ? "matrix" : "records";
        ExcelIO.EnsureDirectoryForFile(req.OutputJsonPath);

        using var fs = System.IO.File.Create(req.OutputJsonPath);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = req.Indented });

        if (mode == "matrix")
        {
            int rcount = range.RowCount();
            int ccount = range.ColumnCount();
            writer.WriteStartArray();
            for (int r = 0; r < rcount; r++)
            {
                writer.WriteStartArray();
                for (int c = 0; c < ccount; c++)
                {
                    var cell = range.Cell(r + 1, c + 1);
                    ExcelIO.WriteJsonCellValue(writer, cell, req.UseFormattedText, req.EmptyAsNull);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.Flush();
            return Results.Ok(new { ok = true, mode, rows = rcount, cols = ccount, path = req.OutputJsonPath });
        }
        else
        {
            int firstCol = range.RangeAddress.FirstAddress.ColumnNumber;
            int lastCol = range.RangeAddress.LastAddress.ColumnNumber;
            int firstRow = range.RangeAddress.FirstAddress.RowNumber;
            int lastRow = range.RangeAddress.LastAddress.RowNumber;

            int headerRow = firstRow;
            if (req.HeaderRow > 0 && req.HeaderRow >= firstRow && req.HeaderRow <= lastRow)
                headerRow = req.HeaderRow;

            int firstDataRow = headerRow + 1;
            int colCount = lastCol - firstCol + 1;

            var headers = new string[colCount];
            var usedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < colCount; i++)
            {
                int c = firstCol + i;
                string h = ws.Cell(headerRow, c).GetString().Trim();
                if (string.IsNullOrWhiteSpace(h))
                    h = ExcelIO.ColumnToLetters(c);
                h = ExcelIO.MakeUniqueHeader(h, usedHeaders);
                headers[i] = h;
            }

            writer.WriteStartArray();
            int wrote = 0;
            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (req.SkipBlankRows && ExcelIO.IsRowBlank(ws, r, firstCol, colCount))
                    continue;

                writer.WriteStartObject();
                for (int i = 0; i < colCount; i++)
                {
                    int c = firstCol + i;
                    writer.WritePropertyName(headers[i]);
                    ExcelIO.WriteJsonCellValue(writer, ws.Cell(r, c), req.UseFormattedText, req.EmptyAsNull);
                }
                writer.WriteEndObject();
                wrote++;
            }
            writer.WriteEndArray();
            writer.Flush();
            return Results.Ok(new { ok = true, mode, rows = wrote, cols = colCount, headerRow, path = req.OutputJsonPath });
        }
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Basic chart listing using OpenXML (read-only)
app.MapPost("/list_charts", (ExcelIO.ListChartsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ExcelPath))
        return Results.BadRequest(new { ok = false, msg = "excelPath is required" });
    try
    {
        var charts = new List<object>();
        using (SpreadsheetDocument doc = SpreadsheetDocument.Open(req.ExcelPath, false))
        {
            var wbPart = doc.WorkbookPart!;
            foreach (var wsPart in wbPart.WorksheetParts)
            {
                string wsName = ExcelIO.GetWorksheetName(wbPart, wsPart);
                foreach (var drawingPart in wsPart.DrawingsPart?.ChartParts ?? Enumerable.Empty<ChartPart>())
                {
                    var chartSpace = drawingPart.ChartSpace;
                    var chart = chartSpace?.GetFirstChild<Chart>();
                    string title = chart?.Title?.ChartText?.InnerText ?? "";
                    charts.Add(new { sheet = wsName, title });
                }
            }
        }
        return Results.Ok(new { ok = true, charts });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, msg = ex.Message });
    }
});

// Print endpoint overview and URL bindings when started
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        var urls = string.Join(", ", app.Urls);
        LogUtil.Info($"[ExcelMCP] Started. Listening on: {urls}");
        var ds = app.Services.GetService<EndpointDataSource>();
        if (ds != null)
        {
            LogUtil.Info("[ExcelMCP] Endpoints:");
            foreach (var ep in ds.Endpoints)
            {
                if (ep is RouteEndpoint re)
                {
                    LogUtil.Info($"  - {re.RoutePattern.RawText}");
                }
            }
        }
    }
    catch (Exception ex)
    {
        LogUtil.Error($"[ExcelMCP] Startup logging error: {ex.Message}");
    }
});
app.Lifetime.ApplicationStopping.Register(() => LogUtil.Info("[ExcelMCP] Stopping..."));
app.Lifetime.ApplicationStopped.Register(() => LogUtil.Info("[ExcelMCP] Stopped."));

LogUtil.Info("[ExcelMCP] Starting web server...");
app.Run();

static class ComUtil
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object? TryGetActiveObject(string progId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(progId)) return null;
            int hr = CLSIDFromProgID(progId, out Guid clsid);
            if (hr != 0) return null;
            hr = GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
            if (hr != 0) return null;
            return obj;
        }
        catch
        {
            return null;
        }
    }

    public static dynamic? FindWorkbook(dynamic excel, string? fullName, string? name)
    {
        try
        {
            dynamic wbs = excel.Workbooks;
            int count = 0;
            try { count = (int)wbs.Count; } catch { count = 0; }

            // Try by FullName first
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                for (int i = 1; i <= count; i++)
                {
                    dynamic wb = null!;
                    try { wb = wbs.Item(i); } catch { continue; }
                    try
                    {
                        string? fn = null;
                        try { fn = (string)wb.FullName; } catch { fn = null; }
                        if (!string.IsNullOrEmpty(fn) && string.Equals(fn, fullName, StringComparison.OrdinalIgnoreCase))
                            return wb;
                    }
                    catch { }
                }
            }

            // Then by Name
            if (!string.IsNullOrWhiteSpace(name))
            {
                for (int i = 1; i <= count; i++)
                {
                    dynamic wb = null!;
                    try { wb = wbs.Item(i); } catch { continue; }
                    try
                    {
                        string? n = null;
                        try { n = (string)wb.Name; } catch { n = null; }
                        if (!string.IsNullOrEmpty(n) && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                            return wb;
                    }
                    catch { }
                }
            }

            // Fallback: if only one workbook, return it
            if (count == 1)
            {
                try { return wbs.Item(1); } catch { }
            }
        }
        catch { }
        return null;
    }

    public static dynamic? ResolveWorksheet(dynamic wb, string? sheetName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                try { return wb.Worksheets.Item(sheetName); } catch { }
                try { return wb.Sheets.Item(sheetName); } catch { }
            }
            try { return wb.ActiveSheet; } catch { }
        }
        catch { }
        return null;
    }

    public static List<List<object?>> ToJagged(object? variant)
    {
        var rows = new List<List<object?>>();
        if (variant is null)
        {
            rows.Add(new List<object?> { null });
            return rows;
        }
        if (variant is Array arr && arr.Rank == 2)
        {
            int r1 = arr.GetLowerBound(0);
            int r2 = arr.GetUpperBound(0);
            int c1 = arr.GetLowerBound(1);
            int c2 = arr.GetUpperBound(1);
            for (int r = r1; r <= r2; r++)
            {
                var line = new List<object?>();
                for (int c = c1; c <= c2; c++)
                {
                    line.Add(arr.GetValue(r, c));
                }
                rows.Add(line);
            }
            return rows;
        }
        // Scalar -> 1x1
        rows.Add(new List<object?> { variant });
        return rows;
    }

    public static object[,] To2DArray(List<List<object?>> values, int rows, int cols)
    {
        var box = new object[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            var line = (r < values.Count && values[r] != null) ? values[r] : new List<object?>();
            for (int c = 0; c < cols; c++)
            {
                object? v = (c < line.Count) ? line[c] : null;
                box[r, c] = Normalize(v);
            }
        }
        return box;
    }

    private static object Normalize(object? v)
    {
        if (v is null) return string.Empty;
        if (v is System.Text.Json.JsonElement je)
        {
            switch (je.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return je.GetString() ?? string.Empty;
                case System.Text.Json.JsonValueKind.Number:
                    if (je.TryGetInt64(out var l)) return (double)l; // Excel expects numeric
                    if (je.TryGetDouble(out var d)) return d;
                    return string.Empty;
                case System.Text.Json.JsonValueKind.True:
                    return true;
                case System.Text.Json.JsonValueKind.False:
                    return false;
                case System.Text.Json.JsonValueKind.Null:
                case System.Text.Json.JsonValueKind.Undefined:
                    return string.Empty;
                case System.Text.Json.JsonValueKind.Object:
                case System.Text.Json.JsonValueKind.Array:
                default:
                    return je.ToString() ?? string.Empty;
            }
        }
        return v;
    }

    public static int CompareCell(List<object?> a, List<object?> b, int col)
    {
        object? av = (col < a.Count) ? a[col] : null;
        object? bv = (col < b.Count) ? b[col] : null;
        // Nulls last
        if (av is null && bv is null) return 0;
        if (av is null) return 1;
        if (bv is null) return -1;

        // Numbers vs numbers
        double? ad = TryNum(av);
        double? bd = TryNum(bv);
        if (ad.HasValue && bd.HasValue)
        {
            return ad.Value.CompareTo(bd.Value);
        }

        // Fallback to string compare
        string as_ = ToStringSafe(av);
        string bs_ = ToStringSafe(bv);
        return string.Compare(as_, bs_, StringComparison.CurrentCulture);
    }

    private static double? TryNum(object v)
    {
        try
        {
            if (v is double dd) return dd;
            if (v is float ff) return (double)ff;
            if (v is int ii) return (double)ii;
            if (v is long ll) return (double)ll;
            if (v is decimal mm) return (double)mm;
            if (v is short ss) return (double)ss;
            if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                if (je.TryGetDouble(out var d)) return d; else return null;
            }
            if (double.TryParse(ToStringSafe(v), out var parse)) return parse;
            return null;
        }
        catch { return null; }
    }

    private static string ToStringSafe(object v)
    {
        try { return v?.ToString() ?? string.Empty; } catch { return string.Empty; }
    }

    public static int ColorFromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return 0;
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return r + (g << 8) + (b << 16); // Excel RGB
        }
        // Try rgb(r,g,b)
        if (hex.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var nums = new List<int>();
            var cur = new System.Text.StringBuilder();
            foreach (var ch in hex)
            {
                if (char.IsDigit(ch)) cur.Append(ch);
                else { if (cur.Length > 0) { if (int.TryParse(cur.ToString(), out var n)) nums.Add(n); cur.Clear(); } }
            }
            if (cur.Length > 0) { if (int.TryParse(cur.ToString(), out var n)) nums.Add(n); }
            if (nums.Count >= 3)
            {
                int r = Math.Clamp(nums[0], 0, 255);
                int g = Math.Clamp(nums[1], 0, 255);
                int b = Math.Clamp(nums[2], 0, 255);
                return r + (g << 8) + (b << 16);
            }
        }
        // Fallback: parse as int
        if (int.TryParse(hex, out var val)) return val;
        return 0;
    }

    public static int HAlign(string? align)
    {
        if (string.IsNullOrWhiteSpace(align)) return -4131; // Left
        align = align.Trim().ToLowerInvariant();
        return align switch
        {
            "center" or "centre" => -4108, // xlCenter
            "right" => -4152, // xlRight
            _ => -4131, // xlLeft
        };
    }

    public static int VAlign(string? align)
    {
        if (string.IsNullOrWhiteSpace(align)) return -4160; // Top
        align = align.Trim().ToLowerInvariant();
        return align switch
        {
            "center" or "middle" => -4108, // xlCenter
            "bottom" => -4107, // xlBottom
            _ => -4160, // xlTop
        };
    }
}

static class ComExcelIO
{
    public record WorkbookSelector
    {
        public string? WorkbookFullName { get; init; }
        public string? WorkbookName { get; init; }
    }

    public record ActivateSheetRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public int? SheetIndex { get; init; }
    }

    public record VbaModuleRequest : WorkbookSelector
    {
        public string? ModuleName { get; init; }
    }

    public record SetVbaModuleRequest : VbaModuleRequest
    {
        public string Code { get; init; } = string.Empty;
    }

    public record ReadCellsRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string RangeA1 { get; init; } = "A1";
        public bool UseValue2 { get; init; } = true;
    }

    public record WriteCellsRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string StartCell { get; init; } = "A1";
        public List<List<object?>> Values { get; init; } = new();
    }

    public record SaveWorkbookRequest : WorkbookSelector
    {
        public string? SaveAsFullName { get; init; }
    }

    public record AppendRowsRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string? StartColumn { get; init; } = "A";
        public List<List<object?>> Rows { get; init; } = new();
    }

    public record FormatRangeRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string Target { get; init; } = "A1";
        public string? NumberFormat { get; init; }
        public string? HorizontalAlign { get; init; } // left|center|right
        public string? VerticalAlign { get; init; }   // top|center|bottom
        public bool? WrapText { get; init; }
        public string? FontName { get; init; }
        public double? FontSize { get; init; }
        public bool? Bold { get; init; }
        public bool? Italic { get; init; }
        public string? FontColor { get; init; } // #RRGGBB or rgb(r,g,b)
        public string? FillColor { get; init; } // #RRGGBB or rgb(r,g,b)
        public double? ColumnWidth { get; init; }
        public double? RowHeight { get; init; }
        public bool? AutoFitColumns { get; init; }
        public bool? AutoFitRows { get; init; }
    }

    public record MergeVerticalHeaderCellsRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string StartColumn { get; init; } = "A";
        public string EndColumn { get; init; } = "BQ";
        public int StartRow { get; init; } = 1; // fixed: 1
        public int EndRow { get; init; } = 3;   // fixed: 3
        public bool DryRun { get; init; } = false;
        public bool AllowMergeTopTwo { get; init; } = true; // If Row1 has text and Row2 blank but Row3 has text, merge 1..2
    }

    public record AddSheetRequest : WorkbookSelector
    {
        public string? NewSheetName { get; init; }
        public string? BeforeSheetName { get; init; }
        public string? AfterSheetName { get; init; }
    }

    public record DeleteSheetRequest : WorkbookSelector
    {
        public string SheetName { get; init; } = string.Empty;
    }

    public record SortRangeRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public string RangeA1 { get; init; } = string.Empty;
        public bool? HasHeader { get; init; }
        public List<SortKey> Keys { get; init; } = new();
    }

    public record SortKey
    {
        public int? ColumnIndex { get; init; }
        public string? ColumnKey { get; init; } // e.g., "B"
        public string? Order { get; init; } // "asc" or "desc"
    }

    public record DeleteEmptyColumnsRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public int HeaderRow { get; init; } = 1;
        public int? DataStartRow { get; init; } // default = HeaderRow + 1
        public List<string>? ProtectHeaders { get; init; }
        public int? MaxDataRows { get; init; } // optional cap (e.g., 300)
        public int? MaxHeaderScanCols { get; init; } // optional cap for header scan (e.g., 400)
    }

    public record DeleteColumnsByIndexRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public List<int> Columns { get; init; } = new();
    }

    public record ResizeRequest : WorkbookSelector
    {
        public string? SheetName { get; init; }
        public List<int>? ColumnIndices { get; init; }
        public double? ColumnWidth { get; init; }
        public bool? AutoFitColumns { get; init; }
        public List<int>? RowIndices { get; init; }
        public double? RowHeight { get; init; }
        public bool? AutoFitRows { get; init; }
    }
}

static class FileAlign
{
    public record AlignHeadersRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? OutputPath { get; init; }
        public string[] Headers { get; init; } = Array.Empty<string>();
        public int HeaderRow { get; init; } = 1;
        public int ScanHeaderCols { get; init; } = 120;
        public int KeyColumn { get; init; } = 1;
        public int MaxDataRows { get; init; } = 500;
        public bool TreatParamNameAsName { get; init; } = true;
    }
}

static class ExcelParser
{
    public static string StyleKey(XLBorderStyleValues s) => s.ToString();

    public record ParseRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public double CellSizeMeters { get; init; } = 1.0;
        public bool? UseColorMask { get; init; }
    }

    public class UnitEdge
    {
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public string? StyleKey { get; set; }
        public bool IsHoriz => Y1 == Y2;
        public bool IsVert => X1 == X2;
    }

    public class LabelOut
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
    }

    public static List<(int X1, int Y1, int X2, int Y2, string StyleKey)> CollapseToMaxSegmentsWithStyle(List<UnitEdge> unitEdges)
    {
        var horiz = new Dictionary<int, Dictionary<int, string>>();
        var vert = new Dictionary<int, Dictionary<int, string>>();

        foreach (var e in unitEdges)
        {
            if (e.IsHoriz)
            {
                int y = e.Y1;
                int x = Math.Min(e.X1, e.X2);
                if (!horiz.TryGetValue(y, out var map)) { map = new Dictionary<int, string>(); horiz[y] = map; }
                map[x] = e.StyleKey ?? "None";
            }
            else if (e.IsVert)
            {
                int x = e.X1;
                int y = Math.Min(e.Y1, e.Y2);
                if (!vert.TryGetValue(x, out var map)) { map = new Dictionary<int, string>(); vert[x] = map; }
                map[y] = e.StyleKey ?? "None";
            }
        }

        var segs = new List<(int, int, int, int, string)>();

        foreach (var kv in horiz)
        {
            int y = kv.Key;
            var xs = kv.Value.Keys.OrderBy(v => v).ToList();
            if (xs.Count == 0) continue;
            int start = xs[0], prev = start;
            string curStyle = "Mixed";

            for (int i = 1; i < xs.Count; i++)
            {
                int x = xs[i];
                bool contiguous = (x == prev + 1);
                if (contiguous)
                {
                    prev = x;
                }
                else
                {
                    segs.Add((start, y, prev + 1, y, curStyle));
                    start = prev = x;
                    curStyle = "Mixed";
                }
            }
            segs.Add((start, y, prev + 1, y, curStyle));
        }

        foreach (var kv in vert)
        {
            int x = kv.Key;
            var ys = kv.Value.Keys.OrderBy(v => v).ToList();
            if (ys.Count == 0) continue;
            int start = ys[0], prev = start;
            string curStyle = "Mixed";

            for (int i = 1; i < ys.Count; i++)
            {
                int y = ys[i];
                bool contiguous = (y == prev + 1);
                if (contiguous)
                {
                    prev = y;
                }
                else
                {
                    segs.Add((x, start, x, prev + 1, curStyle));
                    start = prev = y;
                    curStyle = "Mixed";
                }
            }
            segs.Add((x, start, x, prev + 1, curStyle));
        }

        return segs;
    }
}

static class ExcelIO
{
    public record SheetInfoRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
    }

    public record ReadCellsRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string? RangeA1 { get; init; }
        public bool ReturnRaw { get; init; } = true;
        public bool ReturnFormatted { get; init; } = false;
        public bool IncludeFormula { get; init; } = false;
    }

    public record WriteCellsRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string StartCell { get; init; } = "A1";
        public object?[][]? Values { get; init; }
        public bool TreatNullAsClear { get; init; } = false;
    }

    public record AppendRowsRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string? StartColumn { get; init; } // e.g., A or 1
        public object?[][]? Rows { get; init; }
    }

    public record SetFormulaRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string Target { get; init; } = "A1"; // A1 or A1:B3
        public string FormulaA1 { get; init; } = string.Empty;
    }

    public record FormatRangeFillRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string Target { get; init; } = "A1";
    }

    public record ClearBackgroundByFThresholdRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public double Threshold { get; init; } = 3.0;
        public int HeaderRow { get; init; } = 1; // title row (excluded)
        public string FColumn { get; init; } = "F";
    }

    public record FormatSheetRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public bool? AutoFitColumns { get; init; }
        public bool? AutoFitRows { get; init; }
        public string? Columns { get; init; } // e.g., "A:C, E"
        public string? Rows { get; init; }    // e.g., "1:5, 8"
        public Dictionary<string, double>? ColumnWidths { get; init; } // key: "A" or "1"
        public Dictionary<string, double>? RowHeights { get; init; }   // key: row number as string
        public string? WidthUnit { get; init; } // "characters" (default) or "pixels"
    }

    public record ToCsvRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string OutputCsvPath { get; init; } = "output.csv";
        public string? Delimiter { get; init; }
        public char? Quote { get; init; }
        public bool UseFormattedText { get; init; } = false;
        public string? EncodingName { get; init; } // e.g., "utf-8", "shift_jis"
    }

    public record ToJsonRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
        public string? SheetName { get; init; }
        public string? RangeA1 { get; init; }
        public string OutputJsonPath { get; init; } = "output.json";
        public string Mode { get; init; } = "records"; // "records" (default) or "matrix"
        public int HeaderRow { get; init; } = 1; // used when mode="records" (within selected range)
        public bool UseFormattedText { get; init; } = false;
        public bool Indented { get; init; } = true;
        public bool EmptyAsNull { get; init; } = true;
        public bool SkipBlankRows { get; init; } = true;
    }

    public record ListChartsRequest
    {
        public string ExcelPath { get; init; } = string.Empty;
    }

    public static IXLWorksheet? ResolveWorksheet(XLWorkbook wb, string? sheetName)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
            return wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        return wb.Worksheets.FirstOrDefault();
    }

    public static IXLWorksheet ResolveOrCreateWorksheet(XLWorkbook wb, string? sheetName)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
            return wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                   ?? wb.AddWorksheet(sheetName);
        return wb.Worksheets.FirstOrDefault() ?? wb.AddWorksheet("Sheet1");
    }

    public static IXLRange? ResolveRange(IXLWorksheet ws, string? a1)
    {
        if (string.IsNullOrWhiteSpace(a1)) return null;
        try { return ws.Range(a1); }
        catch { return null; }
    }

    public static int ColumnFromA1(string key)
    {
        if (int.TryParse(key, out var n)) return n;
        int col = 0;
        foreach (var ch in key.ToUpperInvariant().Trim())
        {
            if (ch < 'A' || ch > 'Z') continue;
            col = col * 26 + (ch - 'A' + 1);
        }
        return Math.Max(1, col);
    }

    public static string ColumnToLetters(int col)
    {
        col = Math.Max(1, col);
        var chars = new Stack<char>();
        int n = col;
        while (n > 0)
        {
            n--; // 1-based
            chars.Push((char)('A' + (n % 26)));
            n /= 26;
        }
        return new string(chars.ToArray());
    }

    public static string MakeUniqueHeader(string header, HashSet<string> used)
    {
        var baseName = string.IsNullOrWhiteSpace(header) ? "Column" : header;
        var candidate = baseName;
        int i = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{baseName}_{i}";
            i++;
        }
        return candidate;
    }

    public static bool IsRowBlank(IXLWorksheet ws, int rowNumber, int firstCol, int colCount)
    {
        for (int i = 0; i < colCount; i++)
        {
            if (!ws.Cell(rowNumber, firstCol + i).IsEmpty())
                return false;
        }
        return true;
    }

    public static void EnsureDirectoryForFile(string path)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                System.IO.Directory.CreateDirectory(dir);
        }
        catch { }
    }

    public static void WriteJsonCellValue(Utf8JsonWriter writer, IXLCell cell, bool useFormattedText, bool emptyAsNull)
    {
        if (useFormattedText)
        {
            var text = cell.GetFormattedString();
            if (emptyAsNull && string.IsNullOrEmpty(text))
                writer.WriteNullValue();
            else
                writer.WriteStringValue(text);
            return;
        }

        if (cell.IsEmpty())
        {
            if (emptyAsNull) writer.WriteNullValue();
            else writer.WriteStringValue(string.Empty);
            return;
        }

        try
        {
            switch (cell.DataType)
            {
                case XLDataType.Boolean:
                    writer.WriteBooleanValue(cell.GetBoolean());
                    break;
                case XLDataType.Number:
                    writer.WriteNumberValue(cell.GetDouble());
                    break;
                case XLDataType.DateTime:
                    writer.WriteStringValue(cell.GetDateTime());
                    break;
                case XLDataType.TimeSpan:
                    writer.WriteStringValue(cell.GetTimeSpan().ToString());
                    break;
                default:
                    writer.WriteStringValue(cell.Value.ToString() ?? string.Empty);
                    break;
            }
        }
        catch
        {
            writer.WriteStringValue(cell.Value.ToString() ?? string.Empty);
        }
    }

    public static double ColumnWidthFromPixels(double pixels)
    {
        // Approximate conversion (varies by font/DPI). Excel width unit is roughly number of characters of the default font.
        // Common approximation: width = (pixels - 5) / 7
        return Math.Max(0, (pixels - 5.0) / 7.0);
    }

    public static string CsvEscape(string? value, string delimiter, char quote)
    {
        var s = value ?? string.Empty;
        bool mustQuote = s.Contains(delimiter) || s.Contains('\n') || s.Contains('\r') || s.Contains(quote);
        if (!mustQuote) return s;
        return quote + s.Replace(quote.ToString(), new string(quote, 2)) + quote;
    }

    public static string GetWorksheetName(WorkbookPart wbPart, WorksheetPart wsPart)
    {
        var wb = wbPart.Workbook;
        var sheets = wb.Sheets!.Cast<Sheet>();
        var match = sheets.FirstOrDefault(s => s.Id?.Value == wbPart.GetIdOfPart(wsPart));
        return match?.Name?.Value ?? string.Empty;
    }

    public static void ApplyValue(IXLCell cell, object v)
    {
        switch (v)
        {
            case null:
                cell.Clear();
                break;
            case string s:
                cell.SetValue(s);
                break;
            case bool b:
                cell.SetValue(b);
                break;
            case int i:
                cell.SetValue(i);
                break;
            case long l:
                cell.SetValue(l);
                break;
            case short sh:
                cell.SetValue(sh);
                break;
            case double d:
                cell.SetValue(d);
                break;
            case float f:
                cell.SetValue((double)f);
                break;
            case decimal m:
                cell.SetValue((double)m);
                break;
            case DateTime dt:
                cell.SetValue(dt);
                break;
            default:
                cell.SetValue(v.ToString());
                break;
        }
    }

    public static System.Text.Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return System.Text.Encoding.GetEncoding(name); } catch { return null; }
    }
}

static class LogUtil
{
    private static readonly object _lock = new object();
    private static string? _dir;
    private static string? _file;
    private static DateTime _day;

    public static void Init()
    {
        try
        {
            _dir = Path.Combine(Path.GetTempPath(), "ExcelMCP", "logs");
            Directory.CreateDirectory(_dir);
            Housekeep(_dir, daysToKeep: 7);
            RollFile();
        }
        catch { /* ignore logging init errors */ }
    }

    public static void Info(string msg) => WriteLine(msg);
    public static void Error(string msg) => WriteLine(msg);

    private static void WriteLine(string msg)
    {
        try
        {
            Console.WriteLine(msg);
            if (_dir == null) return;
            lock (_lock)
            {
                if (DateTime.UtcNow.Date != _day) RollFile();
                if (_file != null)
                {
                    File.AppendAllText(_file, $"{DateTime.UtcNow:O} {msg}{Environment.NewLine}");
                }
            }
        }
        catch { /* never crash due to logging */ }
    }

    private static void RollFile()
    {
        _day = DateTime.UtcNow.Date;
        _file = Path.Combine(_dir!, $"excelmcp-{_day:yyyyMMdd}.log");
    }

    private static void Housekeep(string dir, int daysToKeep)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
            foreach (var file in Directory.GetFiles(dir, "*.log"))
            {
                try
                {
                    var last = File.GetLastWriteTimeUtc(file);
                    if (last < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}

