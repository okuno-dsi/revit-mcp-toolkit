# ExcelMCP

A minimal ASP.NET Core Web API for Excel operations. Originally built to parse grid plans (borders as walls) for Revit; now extended with basic read/write, CSV export, formatting, formulas, and simple chart listing.

- Endpoint: `POST /parse_plan`
  - body: `{ "excelPath": "C:/path/plan.xlsx", "sheetName": "Sheet1", "cellSizeMeters": 1.0, "useColorMask": false }`
  - returns: `{ ok, widthCells, heightCells, cellSizeMeters, segmentsDetailed:[{kind,x1,y1,x2,y2,length_m,style}], labels:[{text,x,y}] }`
- Health: `GET /health`

## New Endpoints

- `POST /sheet_info`
  - body: `{ "excelPath": "C:/path/book.xlsx" }`
  - returns: `{ ok, sheets: [{ name, usedRange, firstCell, lastCell, rowCount, columnCount }] }`

- `POST /read_cells`
  - body: `{ excelPath, sheetName?, rangeA1?, returnRaw: true, returnFormatted: false, includeFormula: false }`
  - returns: `{ ok, cells: [{ address, row, column, value?, text?, formula?, dataType }] }`

- `POST /write_cells`
  - body: `{ excelPath, sheetName?, startCell: "A1", values: object[][], treatNullAsClear?: false }`
  - returns: `{ ok, wroteRows, wroteCols }`

- `POST /append_rows`
  - body: `{ excelPath, sheetName?, startColumn?: "A", rows: object[][] }`
  - returns: `{ ok, appendedRows }`

- `POST /set_formula`
  - body: `{ excelPath, sheetName?, target: "A1"|"A1:B3", formulaA1: "=SUM(A1:A10)" }`
  - returns: `{ ok, cells }`

- `POST /format_sheet`
  - body: `{ excelPath, sheetName?, autoFitColumns?, autoFitRows?, columns?: "A:C", rows?: "1:10", columnWidths?: { "A": 15.5 }, rowHeights?: { "2": 22.0 }, widthUnit?: "characters"|"pixels" }`
  - returns: `{ ok }`

- `POST /to_csv`
  - body: `{ excelPath, sheetName?, outputCsvPath, delimiter?: ",", quote?: '"', useFormattedText?: false }`
  - returns: `{ ok, rows, cols, path }`

- `POST /to_json`
  - body: `{ excelPath, sheetName?, rangeA1?, outputJsonPath, mode?: "records"|"matrix", headerRow?: 1, useFormattedText?: false, indented?: true, emptyAsNull?: true, skipBlankRows?: true }`
  - returns: `{ ok, mode, rows, cols, path, headerRow? }`

- `POST /list_charts`
  - body: `{ excelPath }`
  - returns: `{ ok, charts: [{ sheet, title }] }` (read-only; via OpenXML)

## Build & Run

- Build: `dotnet build`
- Run (dev): `dotnet run --urls http://localhost:5215`

## Notes
- Uses ClosedXML for Excel I/O. Charts are listed via OpenXML SDK (read-only).
- Coordinates are meters, origin at bottom-left of scan region.
- When `useColorMask=true`, the scan window is restricted to cells with non-white fill.
