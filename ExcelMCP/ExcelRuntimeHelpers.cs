using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class RuntimePaths
{
    private static string[] _args = Array.Empty<string>();

    public static string LogDirectory { get; private set; } = GetDefaultLogDirectory();
    public static string PidFilePath { get; private set; } = GetDefaultPidFilePath();

    public static void Configure(string[] args)
    {
        _args = args ?? Array.Empty<string>();
        LogDirectory = ResolvePath("--log-dir", "EXCELMCP_LOG_DIR", GetDefaultLogDirectory());
        PidFilePath = ResolvePath("--pid-file", "EXCELMCP_PID_FILE", GetDefaultPidFilePath());
    }

    public static void WritePidFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(PidFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var payload = new
            {
                pid = Environment.ProcessId,
                startedUtc = DateTime.UtcNow,
                logDirectory = LogDirectory,
                processPath = Environment.ProcessPath,
                commandLine = Environment.CommandLine
            };
            File.WriteAllText(PidFilePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch { }
    }

    public static void TryDeletePidFile()
    {
        try
        {
            if (!File.Exists(PidFilePath))
                return;

            var content = File.ReadAllText(PidFilePath);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("pid", out var pidNode) && pidNode.TryGetInt32(out var pid) && pid == Environment.ProcessId)
            {
                File.Delete(PidFilePath);
            }
        }
        catch { }
    }

    private static string ResolvePath(string argName, string envName, string fallback)
    {
        var fromArg = GetArgValue(argName);
        if (!string.IsNullOrWhiteSpace(fromArg))
            return fromArg!;

        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv!;

        return fallback;
    }

    private static string? GetArgValue(string argName)
    {
        for (var i = 0; i < _args.Length; i++)
        {
            var arg = _args[i];
            if (arg.StartsWith(argName + "=", StringComparison.OrdinalIgnoreCase))
                return arg[(argName.Length + 1)..].Trim('"');

            if (string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase) && i + 1 < _args.Length)
                return _args[i + 1].Trim('"');
        }

        return null;
    }

    private static string GetDefaultLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Revit_MCP", "Logs", "ExcelMCP");

    private static string GetDefaultPidFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Revit_MCP", "Run", "ExcelMCP.pid");
}

internal sealed class ResolvedReadablePath : IDisposable
{
    private readonly string? _temporaryPath;

    public ResolvedReadablePath(string effectivePath, string? temporaryPath = null)
    {
        EffectivePath = effectivePath;
        _temporaryPath = temporaryPath;
    }

    public string EffectivePath { get; }
    public bool UsedLiveCopy => !string.IsNullOrWhiteSpace(_temporaryPath);

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_temporaryPath))
            return;

        try
        {
            if (File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
        }
        catch { }
    }
}

internal static class ExcelAccessUtil
{
    public static ResolvedReadablePath ResolveReadablePath(string excelPath)
    {
        if (string.IsNullOrWhiteSpace(excelPath))
            throw new ArgumentException("excelPath is required", nameof(excelPath));

        if (!File.Exists(excelPath))
            throw new FileNotFoundException("Excel file not found", excelPath);

        if (CanOpenRead(excelPath))
            return new ResolvedReadablePath(excelPath);

        var liveCopy = ComUtil.TrySaveOpenWorkbookCopy(excelPath, out var liveError);
        if (!string.IsNullOrWhiteSpace(liveCopy))
            return new ResolvedReadablePath(liveCopy!, liveCopy);

        throw new IOException(liveError ?? $"Excel file is locked and no open workbook fallback was available: {excelPath}");
    }

    public static bool IsLockConflict(Exception ex)
    {
        if (ex is IOException ioEx)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            return ioEx.HResult == sharingViolation || ioEx.HResult == lockViolation;
        }

        return ex is UnauthorizedAccessException;
    }

    public static IResult? TryWriteCellsViaLiveWorkbook(ExcelIO.WriteCellsRequest req)
    {
        if (req.Values is null || req.Values.Length == 0)
            return Results.BadRequest(new { ok = false, msg = "values must be a 2D array with at least 1 row" });

        return WithWorkbookAndSheet(req.ExcelPath, req.SheetName, (excel, wb, ws) =>
        {
            dynamic? start = null;
            dynamic? finish = null;
            dynamic? target = null;
            try
            {
                start = ws.Range(req.StartCell);
                int rows = req.Values.Length;
                int cols = req.Values.Max(r => r?.Length ?? 0);
                finish = ws.Cells[(int)start.Row + rows - 1, (int)start.Column + cols - 1];
                target = ws.Range(start, finish);
                var values = ToCom2DArray(req.Values, rows, cols, req.TreatNullAsClear);
                try { target.Value2 = values; } catch { target.Value = values; }
                try { wb.Save(); } catch { }
                return Results.Ok(new { ok = true, wroteRows = rows, wroteCols = cols, usedComFallback = true });
            }
            finally
            {
                ReleaseCom(target);
                ReleaseCom(finish);
                ReleaseCom(start);
            }
        });
    }

    public static IResult? TryAppendRowsViaLiveWorkbook(ExcelIO.AppendRowsRequest req)
    {
        if (req.Rows is null || req.Rows.Length == 0)
            return Results.BadRequest(new { ok = false, msg = "rows must be provided" });

        return WithWorkbookAndSheet(req.ExcelPath, req.SheetName, (excel, wb, ws) =>
        {
            dynamic? c1 = null;
            dynamic? c2 = null;
            dynamic? target = null;
            try
            {
                int startCol = string.IsNullOrWhiteSpace(req.StartColumn) ? 1 : ExcelIO.ColumnFromA1(req.StartColumn!);
                int rowCount;
                try { rowCount = (int)ws.Rows.Count; } catch { rowCount = 1048576; }
                int lastRow;
                try { lastRow = (int)ws.Cells[rowCount, startCol].End(-4162).Row; } catch { lastRow = 1; }
                int nextRow = lastRow;
                object? firstCellValue = null;
                try { firstCellValue = ws.Cells[1, startCol].Value2; } catch { }
                if (lastRow <= 1 && firstCellValue is null)
                    nextRow = 1;
                else
                    nextRow = lastRow + 1;

                int rows = req.Rows.Length;
                int cols = req.Rows.Max(r => r?.Length ?? 0);
                c1 = ws.Cells[nextRow, startCol];
                c2 = ws.Cells[nextRow + rows - 1, startCol + cols - 1];
                target = ws.Range(c1, c2);
                var values = ToCom2DArray(req.Rows, rows, cols, treatNullAsClear: false);
                try { target.Value2 = values; } catch { target.Value = values; }
                try { wb.Save(); } catch { }
                return Results.Ok(new { ok = true, appendedRows = rows, usedComFallback = true });
            }
            finally
            {
                ReleaseCom(target);
                ReleaseCom(c2);
                ReleaseCom(c1);
            }
        });
    }

    public static IResult? TrySetFormulaViaLiveWorkbook(ExcelIO.SetFormulaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Target))
            return Results.BadRequest(new { ok = false, msg = "target is required (A1 or A1:B3)" });
        if (string.IsNullOrWhiteSpace(req.FormulaA1))
            return Results.BadRequest(new { ok = false, msg = "formulaA1 is required" });

        return WithWorkbookAndSheet(req.ExcelPath, req.SheetName, (excel, wb, ws) =>
        {
            dynamic? range = null;
            try
            {
                range = ws.Range(req.Target);
                foreach (dynamic cell in range.Cells())
                {
                    try { cell.Formula = req.FormulaA1; } catch { cell.FormulaLocal = req.FormulaA1; }
                    ReleaseCom(cell);
                }
                try { wb.Save(); } catch { }
                int count = ((int)range.Rows.Count) * ((int)range.Columns.Count);
                return Results.Ok(new { ok = true, cells = count, usedComFallback = true });
            }
            finally
            {
                ReleaseCom(range);
            }
        });
    }

    private static IResult WithWorkbookAndSheet(string excelPath, string? sheetName, Func<dynamic, dynamic, dynamic, IResult> action)
    {
        object? excelObj = ComUtil.TryGetActiveObject("Excel.Application");
        if (excelObj is null)
            return Results.Conflict(new { ok = false, msg = "Excel workbook is locked and Excel is not running for COM fallback." });

        dynamic excel = excelObj;
        dynamic? wb = null;
        dynamic? ws = null;
        try
        {
            string? workbookError;
            wb = ComUtil.FindWorkbookStrict(excel, excelPath, Path.GetFileName(excelPath), out workbookError);
            if (wb is null)
                return Results.Conflict(new { ok = false, msg = workbookError ?? "target workbook not found for COM fallback" });

            string? worksheetError;
            ws = ComUtil.ResolveWorksheetStrict(wb, sheetName, out worksheetError);
            if (ws is null)
                return Results.BadRequest(new { ok = false, msg = worksheetError ?? "target worksheet not found" });

            return action(excel, wb, ws);
        }
        catch (Exception ex)
        {
            return Results.Ok(new { ok = false, msg = ex.Message, usedComFallback = true });
        }
        finally
        {
            ReleaseCom(ws);
            ReleaseCom(wb);
            ReleaseCom(excel);
        }
    }

    private static bool CanOpenRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object[,] ToCom2DArray(object?[][] values, int rows, int cols, bool treatNullAsClear)
    {
        var box = new object[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            var row = values[r] ?? Array.Empty<object?>();
            for (int c = 0; c < cols; c++)
            {
                var v = c < row.Length ? row[c] : null;
                box[r, c] = Normalize(v, treatNullAsClear);
            }
        }
        return box;
    }

    private static object Normalize(object? value, bool treatNullAsClear)
    {
        if (value is null)
            return treatNullAsClear ? string.Empty : string.Empty;

        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString() ?? string.Empty,
                JsonValueKind.Number when json.TryGetInt64(out var l) => (double)l,
                JsonValueKind.Number when json.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => string.Empty,
                _ => json.ToString() ?? string.Empty
            };
        }

        return value;
    }

    private static void ReleaseCom(object? obj)
    {
        try
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }
        catch { }
    }
}
