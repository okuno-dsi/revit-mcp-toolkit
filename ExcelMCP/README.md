# ExcelMCP

ExcelMCP is an ASP.NET Core Minimal API for Excel operations and MCP-based tool access.
It provides both direct HTTP endpoints and MCP tools for workbook inspection, read/write operations, export, formatting, and selected COM automation tasks.

## Current MCP Scope
- Transport: `OPTIONS /mcp`, `GET /mcp`, `POST /mcp`, `DELETE /mcp`
- Default protocol version: `2025-11-25`
- Supported protocol versions:
  - `2025-11-25`
  - `2025-11-05`
  - `2025-03-26`
- Session lifecycle:
  1. `initialize`
  2. `notifications/initialized`
  3. `tools/list` / `tools/call`
- Notifications do not receive JSON-RPC response bodies.
- Batch JSON-RPC requests are supported.

## First-Class MCP Tools
File-based tools are loaded from `mcp_commands.jsonl` and exposed as `excel.*` tools.
Main tools include:
- `excel.health`
- `excel.parse_plan`
- `excel.sheet_info`
- `excel.read_cells`
- `excel.write_cells`
- `excel.append_rows`
- `excel.set_formula`
- `excel.format_sheet`
- `excel.to_csv`
- `excel.to_json`
- `excel.list_charts`

Additional manual tools are registered for COM and MCP support:
- `excel.list_open_workbooks`
- `excel.com.activate_workbook`
- `excel.com.activate_sheet`
- `excel.com.read_cells`
- `excel.com.write_cells`
- `excel.com.append_rows`
- `excel.com.save_workbook`
- `excel.com.format_range`
- `excel.com.add_sheet`
- `excel.com.delete_sheet`
- `excel.com.sort_range`
- `excel.preview_write_cells`
- `excel.preview_append_rows`
- `excel.preview_set_formula`
- `excel.api_call`
- `mcp.status`

`excel.api_call` remains for compatibility, but new clients should prefer the first-class tools above.

## Core HTTP Endpoints
- `GET /health`
- `GET /list_open_workbooks`
- `POST /sheet_info`
- `POST /read_cells`
- `POST /write_cells`
- `POST /append_rows`
- `POST /set_formula`
- `POST /format_sheet`
- `POST /to_csv`
- `POST /to_json`
- `POST /list_charts`
- `POST /parse_plan`

COM endpoints remain available under `/com/*`.

## Build and Test
```powershell
dotnet clean ExcelMCP.sln
dotnet build ExcelMCP.sln -c Release
dotnet test ExcelMCP.sln -c Release --no-build
```

Or run the consolidated release script:
```powershell
pwsh .\publish_release.ps1
```

## Run
```powershell
dotnet run --project ExcelMCP --configuration Release --urls http://localhost:5215
```

## Notes
- File-based Excel I/O uses ClosedXML.
- Chart listing uses OpenXML SDK.
- COM endpoints operate on already open Excel workbooks.
- The canonical release process is documented in `BUILD_RELEASE.md`.
- `publish_release.ps1` runs clean/build/test/publish in one flow.
- Japanese operations manual: `MANUAL_JA.md`
