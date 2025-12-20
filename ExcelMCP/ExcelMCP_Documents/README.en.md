# ExcelMCP Server Manual (English)

ExcelMCP is a minimal ASP.NET Core Web API for inspecting and manipulating Excel workbooks via HTTP.

- **Excel COM endpoints**: Automate a running desktop Excel instance (Excel must be installed and running).
- **File-based endpoints**: Read/write `.xlsx` directly via ClosedXML/OpenXML (no Excel process required).

## Prerequisites

- Windows
- .NET 8 SDK
- For COM endpoints: Microsoft Excel (desktop) installed and running in the same interactive user session.

## Start the server

Run from the `ExcelMCP` folder:

```
# Development (Swagger UI enabled)
dotnet run --urls http://localhost:5215

# Release
dotnet run -c Release --urls http://localhost:5215

# PowerShell helper
pwsh .\start.ps1 -Port 5215
```

Health check:

```
Invoke-RestMethod http://localhost:5215/health | ConvertTo-Json -Compress
```

## Logs

- Logs are written to console and also to:
  - `%TEMP%\ExcelMCP\logs\excelmcp-YYYYMMDD.log`
- Each request logs `[REQ]` / `[RES]`, and exceptions log `[ERR]`.

## Common conventions (important)

- Many endpoints may return HTTP 200 with `{ "ok": false, "msg": "..." }`. Always check `ok`.
- JSON uses `camelCase` field names (e.g., `workbookFullName`, `sheetName`, `rangeA1`).
- Prefer absolute paths (e.g., `C:\\path\\book.xlsx`).
- File-based endpoints open and save `.xlsx`. They may fail if the file is open/locked by Excel.
- A1 notation:
  - Cell: `B2`
  - Range: `A1:C10`
  - Row range: `1:1` (COM side only)

## Endpoint summary (as of Program.cs)

### Runtime

- `GET /` server status
- `GET /health` health check

### Excel COM (Excel must be running)

- `GET /list_open_workbooks` list open workbooks
- `POST /com/activate_workbook` activate a workbook window
- `POST /com/activate_sheet` activate a worksheet
- `POST /com/read_cells` read a range
- `POST /com/write_cells` write a 2D array starting from a cell
- `POST /com/append_rows` append rows under the first empty row in a column
- `POST /com/format_range` format a range (alignment/font/fill/width/height/autofit)
- `POST /com/save_workbook` save or save-as
- `POST /com/add_sheet` add a worksheet
- `POST /com/delete_sheet` delete a worksheet
- `POST /com/sort_range` sort a range (Excel-native with fallback)
- `POST /com/delete_empty_columns` delete headered columns with no data below
- `POST /com/delete_columns_by_index` delete columns by 1-based indices
- `POST /com/resize` resize columns/rows by indices
- `POST /com/merge_vertical_header_cells` merge vertical header cells (rows 1..3)
- `POST /com/get_vba_module` get VBA module code (requires macro security setting)
- `POST /com/set_vba_module` overwrite VBA module code (requires macro security setting)

### File-based (no Excel process)

- `POST /file/align_headers` create a new file aligned to a header order
- `POST /sheet_info` list sheets and used-range info
- `POST /read_cells` read used cells in a range/used range
- `POST /write_cells` write values and save
- `POST /append_rows` append rows and save
- `POST /set_formula` set formulas and save
- `POST /format_sheet` set widths/heights/autofit and save
- `POST /format_range_fill` fill a range with white (currently white only)
- `POST /clear_bg_by_f_threshold` clear A:J background when F<=threshold
- `POST /to_csv` export to CSV
- `POST /to_json` export to JSON (records/matrix)
- `POST /list_charts` list charts (OpenXML, read-only)
- `POST /parse_plan` parse grid borders/labels (for Revit workflows)

## COM endpoints: workbook selector

COM endpoints locate targets inside the running Excel instance.

- `workbookFullName` (recommended): full file path
- `workbookName`: workbook window caption/name
- If both are omitted: `ActiveWorkbook` is used

### GET /list_open_workbooks

Response example:

```json
{
  "ok": true,
  "found": true,
  "workbooks": [
    { "name": "Book1.xlsx", "fullName": "C:\\path\\Book1.xlsx", "path": "C:\\path", "saved": true }
  ]
}
```

### POST /com/activate_workbook

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx" }
```

### POST /com/activate_sheet

Specify `sheetName` or `sheetIndex` (1-based). If omitted, activates the ActiveSheet.

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1" }
```

### POST /com/read_cells

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A1:C5",
  "useValue2": true
}
```

Response example:

```json
{ "ok": true, "rows": [["A1", "B1", "C1"], ["A2", 123, true]] }
```

### POST /com/write_cells

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startCell": "A10",
  "values": [["API test", 123], ["Next", 456]]
}
```

Response example:

```json
{ "ok": true, "wroteRows": 2, "wroteCols": 2 }
```

### POST /com/append_rows

Appends under the first empty row in `startColumn`.

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startColumn": "A",
  "rows": [["r1c1", "r1c2"], ["r2c1", "r2c2"]]
}
```

Response example:

```json
{ "ok": true, "appendedRows": 2, "toRow": 42 }
```

### POST /com/format_range

`target` is an A1 range. Colors accept `#RRGGBB` or `rgb(r,g,b)`.

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "target": "A1:C10",
  "numberFormat": "General",
  "horizontalAlign": "center",
  "verticalAlign": "center",
  "wrapText": true,
  "fontName": "Calibri",
  "fontSize": 11,
  "bold": true,
  "italic": false,
  "fontColor": "#000000",
  "fillColor": "#FFF2CC",
  "columnWidth": 18,
  "rowHeight": 22,
  "autoFitColumns": true,
  "autoFitRows": true
}
```

### POST /com/save_workbook

Save:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx" }
```

Save-as:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "saveAsFullName": "C:\\path\\Book1_copy.xlsx" }
```

### POST /com/add_sheet

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "newSheetName": "NewSheet",
  "afterSheetName": "Sheet1"
}
```

Response example:

```json
{ "ok": true, "name": "NewSheet" }
```

### POST /com/delete_sheet

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet2" }
```

### POST /com/sort_range

`keys` supports either `columnIndex` (1-based inside the range) or `columnKey` (e.g., `"B"`).

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A2:E100",
  "hasHeader": true,
  "keys": [{ "columnIndex": 1, "order": "asc" }, { "columnKey": "C", "order": "desc" }]
}
```

Response example:

```json
{ "ok": true, "engine": "excel" }
```

(If Excel-native sort fails, it falls back to in-memory value sorting with `engine: "fallback"`.)

### POST /com/delete_empty_columns

Deletes columns that have a header but no data between `dataStartRow..lastDataRow`.

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "headerRow": 1,
  "dataStartRow": 2,
  "maxDataRows": 400,
  "maxHeaderScanCols": 100,
  "protectHeaders": ["elementId"]
}
```

Response example:

```json
{
  "ok": true,
  "deletedCount": 3,
  "deleted": [{ "column": 7, "header": "foo" }],
  "headerRow": 1,
  "dataStartRow": 2,
  "lastDataRow": 120,
  "lastHeaderCol": 40
}
```

### POST /com/delete_columns_by_index

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "columns": [23, 24, 25] }
```

Response example:

```json
{ "ok": true, "deleted": 3 }
```

### POST /com/resize

Indices are 1-based. If width/height is omitted, the endpoint defaults to AutoFit.

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "columnIndices": [1, 2, 3], "columnWidth": 18 }
```

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "rowIndices": [1, 2, 3], "autoFitRows": true }
```

Response example:

```json
{ "ok": true, "colsChanged": 3, "rowsChanged": 0 }
```

### POST /com/merge_vertical_header_cells

Merges a 3-row header vertically (rows 1..3). Already-merged cells are skipped.

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startColumn": "A",
  "endColumn": "BQ",
  "dryRun": true,
  "allowMergeTopTwo": true
}
```

Response example:

```json
{ "ok": true, "dryRun": true, "mergedCount": 12, "skippedCount": 3, "mergedRanges": ["A1:A3", "B2:B3"] }
```

### POST /com/get_vba_module / POST /com/set_vba_module

Reading/writing VBA requires Excel macro security setting:
“Trust access to the VBA project object model”.

Get:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsm", "moduleName": "Module1" }
```

Response example:

```json
{ "ok": true, "moduleName": "Module1", "code": "Sub Hello()\\nEnd Sub\\n" }
```

Set:

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsm",
  "moduleName": "Module1",
  "code": "Sub Hello()\\nMsgBox \\\"Hi\\\"\\nEnd Sub\\n"
}
```

## File-based endpoints

### POST /file/align_headers (creates a new file)

- Reads `excelPath` and writes a new `.xlsx` (`outputPath` or append `R` before extension).
- Ensures `headerRow` equals the provided `headers`.
- Copies values by matching header names (case-insensitive).
- If `treatParamNameAsName=true`, fills `name` from `paramName` when needed.

```json
{
  "excelPath": "C:\\path\\Source.xlsx",
  "outputPath": null,
  "headers": ["elementId", "paramKind", "name", "value"],
  "headerRow": 1,
  "scanHeaderCols": 120,
  "keyColumn": 1,
  "maxDataRows": 500,
  "treatParamNameAsName": true
}
```

Response example:

```json
{ "ok": true, "output": "C:\\path\\SourceR.xlsx" }
```

### POST /sheet_info

```json
{ "excelPath": "C:\\path\\book.xlsx" }
```

Response example:

```json
{
  "ok": true,
  "sheets": [
    { "name": "Sheet1", "usedRange": "A1:F20", "firstCell": "A1", "lastCell": "F20", "rowCount": 20, "columnCount": 6 }
  ]
}
```

### POST /read_cells

Returns “used cells” (`CellsUsed`) in the specified range, or in UsedRange if `rangeA1` is omitted.

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "rangeA1": "A1:C5", "returnRaw": true, "returnFormatted": false, "includeFormula": true }
```

Response example:

```json
{ "ok": true, "cells": [{ "address": "B2", "row": 2, "column": 2, "value": "123.45", "text": null, "formula": "=SUM(A1:A10)", "dataType": "Number" }] }
```

### POST /write_cells (in-place)

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "startCell": "B2", "values": [[1, 2, 3], ["a", "b", "c"]], "treatNullAsClear": false }
```

### POST /append_rows

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "startColumn": "A", "rows": [["X", "Y", "Z"], [10, 20, 30]] }
```

### POST /set_formula

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "target": "E2:E3", "formulaA1": "=SUM(B2:D2)" }
```

### POST /format_sheet

```json
{
  "excelPath": "C:\\path\\book.xlsx",
  "sheetName": "Sheet1",
  "autoFitColumns": true,
  "columnWidths": { "A": 18, "C": 120 },
  "widthUnit": "pixels"
}
```

### POST /format_range_fill (currently white fill only)

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "target": "A1:C10" }
```

### POST /clear_bg_by_f_threshold

For rows where the F column (default `F`) value is `<= threshold`, fills A..J background with white.

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "threshold": 3.0, "headerRow": 1, "fColumn": "F" }
```

### POST /to_csv

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "outputCsvPath": "C:\\tmp\\out.csv", "delimiter": ",", "quote": "\"", "useFormattedText": false, "encodingName": "utf-8" }
```

### POST /to_json

- `mode="records"`: emits an array of objects using `headerRow` as keys
- `mode="matrix"`: emits a 2D array
- If `rangeA1` is omitted, Uses UsedRange

```json
{
  "excelPath": "C:\\path\\book.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A1:E100",
  "outputJsonPath": "C:\\tmp\\out.json",
  "mode": "records",
  "headerRow": 1,
  "useFormattedText": false,
  "indented": true,
  "emptyAsNull": true,
  "skipBlankRows": true
}
```

### POST /list_charts (read-only)

```json
{ "excelPath": "C:\\path\\book.xlsx" }
```

### POST /parse_plan

Extracts border segments and cell text labels for grid-plan parsing workflows.

```json
{ "excelPath": "C:\\path\\plan.xlsx", "sheetName": "Sheet1", "cellSizeMeters": 1.0, "useColorMask": false }
```

## Test script

`ExcelMCP/test_requests.ps1` exercises the main endpoints:

```
pwsh -ExecutionPolicy Bypass -File .\test_requests.ps1 -BaseUrl http://localhost:5215 -ExcelPath C:\path\book.xlsx
```

## Troubleshooting

- `Excel is not running` (COM): start Excel first (same user/desktop session).
- `RPC_E_SERVERCALL_RETRYLATER` (COM): Excel is busy; retry after a short wait.
- `VBProject not accessible` (VBA): enable “Trust access to the VBA project object model”.
- File-based endpoints fail: ensure the `.xlsx` is not open/locked by Excel.
