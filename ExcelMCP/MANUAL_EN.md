## ExcelMCP Operations Manual (EN)

This guide explains how to run, test, and use ExcelMCP’s HTTP API for agents. It preserves legacy `/parse_plan` behavior and adds read/write, append, CSV export, formatting, formulas, and simple chart listing.

### 1. Overview
- Purpose: Excel workbook operations exposed via HTTP API.
- Keep: Existing `/parse_plan` for grid plan parsing remains unchanged.
- Tech: ASP.NET Core Minimal API, ClosedXML; OpenXML SDK for read-only chart listing.

### 2. Start the Server
1) Ensure .NET 8 SDK is installed.
2) Start on a chosen port (e.g., 5215):
   - `dotnet run --project ExcelMCP --configuration Release --urls http://localhost:5215`
3) Healthy startup indicators in console:
   - `[ExcelMCP] Building application...`
   - `Now listening on: http://localhost:5215`
   - `[ExcelMCP] Endpoints:` followed by the list
4) Health check:
   - `curl http://localhost:5215/health` → 200 OK.

Tip: You can also launch via `ExcelMCP/start.ps1 -Port 5215` (PowerShell).

### 3. Key Endpoints
- `GET /health` Server health
- `POST /sheet_info` List sheets and used-range info
- `POST /read_cells` Read a rectangular range or used range
- `POST /write_cells` Write a 2D array starting from a cell
- `POST /append_rows` Append rows under last used row
- `POST /set_formula` Set A1-style formulas to a cell/range
- `POST /format_sheet` Autofit columns/rows; set widths/heights
- `POST /to_csv` Export a worksheet to CSV (uses `encodingName`)
- `POST /list_charts` List charts (sheet and title)
- Legacy: `POST /parse_plan` Grid borders/labels extraction

Refer to `ExcelMCP/README.md` for payload details.

### 4. End-to-End Test
- Script: `ExcelMCP/test_requests.ps1`
- Example:
  - `pwsh ExcelMCP/test_requests.ps1 -BaseUrl http://localhost:5215`
  - The script picks a `.xlsx` from the repo, copies to a temp folder, and calls:
    - `/sheet_info` → `/write_cells` → `/append_rows` → `/set_formula` → `/format_sheet` → `/to_csv` → `/read_cells` → `/list_charts`
  - All steps print `[OK]` when successful.

### 5. Logs & Debugging
- Startup logs
  - Show environment, bound URLs, endpoint list.
- Request logs
  - Every HTTP request logs `[REQ]` on entry and `[RES]` with status and elapsed ms; errors log as `[ERR]` with type and message.
- File output with rotation
  - In addition to console, logs are written to:
    - Directory: `%TEMP%\ExcelMCP\logs`
    - Filename: `excelmcp-YYYYMMDD.log`
  - Retention: 7 days (older `.log` removed on startup)
- Quick checks (PowerShell)
  - Current day tail: ``Get-Content "$env:TEMP\ExcelMCP\logs\excelmcp-$(Get-Date -Format yyyyMMdd).log" -Tail 50``
  - Latest log tail: ``$d=Join-Path $env:TEMP 'ExcelMCP\logs'; Get-ChildItem $d -Filter 'excelmcp-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | % { Get-Content $_.FullName -Tail 50 }``
- Notes
  - For `/to_csv`, use `encodingName` (e.g., `utf-8`, `shift_jis`). Do not send raw Encoding objects in JSON.
  - Write operations overwrite existing cells. `append_rows` appends at “last used row + 1”.

### 6. MCP Commands Spec
- File: `ExcelMCP/mcp_commands.en.jsonl`
  - One JSON object per line: `name`, `method`, `path`, `description`, `input_schema`, `output_example`.
  - Note: `/to_csv` supports `encodingName` in the input.

### 7. Compatibility
- `/parse_plan` is unchanged for existing Revit workflows.

### 8. Troubleshooting
- No response after start / exceptions on first request
  - Inspect console or log file for `[ERR]` lines and the developer exception details.
  - If build or package resolution fails, run `dotnet restore` then `dotnet build -c Release`.
- Port in use
  - Change `--urls` to a free port, e.g., `http://localhost:5220`.

