# Excel → Revit Walls & Rooms (MCP) — Durable Workflow

This guide describes a robust, repeatable workflow to create Revit walls and rooms from an Excel plan using the Revit MCP add‑in and the companion ExcelMCP parser service. Durable (jobId) request flow is the default.

## Overview
- Excel parsing is done by ExcelMCP (ASP.NET Core Web API). It reads cell borders as line segments and cell text as labels.
- Revit communication is via the MCP add‑in on a local HTTP port (default `5210`).
- Walls are built from parsed segments with de‑duplication (merge collinear/touching intervals).
- Rooms are created only where Excel provides a label (no auto‑fill). Label points are shifted to the cell center.

## Prerequisites
- Revit running with the MCP add‑in (server port default: `5210`).
- ExcelMCP running (e.g., `http://localhost:5216`).
- PowerShell and Python available.
- Repository files:
  - `Manuals/Scripts/send_revit_command_durable.py` (durable job sender; default)
  - Helper scripts referenced below.

## Services Health Check
- Revit MCP: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server`
- ExcelMCP: open `http://localhost:5216/health` and verify `{ ok: true }`.

## Excel Parsing (ExcelMCP)
POST `http://localhost:5216/parse_plan`

Request body:
```
{ "excelPath": "C:/path/plan.xlsx", "sheetName": "Sheet1", "cellSizeMeters": 1.0, "useColorMask": true }
```
Key fields:
- `segmentsDetailed`: `{ kind, x1,y1,x2,y2, length_m, style }` (meters)
- `labels`: `{ text, x, y }` (meters; cell center)
- `scanRegion`: `{ firstRow, firstCol, lastRow, lastCol }`
- `originBorderCell`: `{ row, col, address }`

Notes
- Excel gridlines are NOT borders. Apply explicit borders to cells in Excel.
- `useColorMask: true` restricts scan to colored cells; recommended to avoid stray borders.

## Coordinate System & Alignment
To avoid wall/room misalignment, compute a single origin and apply the same translation for walls and rooms.

Two choices (pick ONE and use consistently):
1) Global min of all segment coordinates (recommended for cached replays)
2) Bottom‑left border cell via `originBorderCell` and `scanRegion`

Cell center shift for room labels: `centerShiftMm = (cellSizeMeters * 1000) / 2`.

## Walls — Creation Flow
1) Parse Excel via ExcelMCP, get `segmentsDetailed`.
2) Translate to mm using the chosen origin and multiply meters by 1000.
3) Merge collinear intervals per line (H/V separately) with a small tolerance (e.g., 1mm) to prevent duplicates.
4) Create walls one by one with small delays to avoid UI stalls.

Example (one wall) using Durable sender:
```powershell
$p = @{ start = @{ x = 0; y = 0; z = 0 }; end = @{ x = 3000; y = 0; z = 0 }; baseLevelName = '1FL'; heightMm = 3000; wallTypeName = 'RC150'; __smoke_ok = $true } | ConvertTo-Json -Compress
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_wall --params $p --wait-seconds 120
```

## Rooms — Creation Flow
1) Use Excel labels only (no auto‑fill).
2) Compute the room point (mm) from label using the same origin + center‑of‑cell shift.
3) Create rooms sequentially with `levelId` (prefer ID over name), then set the room Name.

Defaults and results
- Default options: `autoTag=true`, `strictEnclosure=true`, `checkExisting=true`.
- If a room already exists at the point (same level), returns `ok:true, mode:"existing"` with `elementId`.
- On unenclosed regions, strict mode returns `ok:false, code:"NOT_ENCLOSED"`.

Example (create and then set name):
```powershell
# Resolve levelId
$levels = (python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_levels | ConvertFrom-Json).result.result.levels
$levelId = ($levels | Where-Object { $_.name -eq '1FL' } | Select-Object -First 1).levelId

# Create room
$create = @{ levelId = [int]$levelId; x = 1500; y = 1500; __smoke_ok = $true } | ConvertTo-Json -Compress
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_room --params $create --wait-seconds 120

# Get a room id from result or list and set Name
$rid = (python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_rooms --params '{"skip":0,"count":50}' | ConvertFrom-Json).result.result.rooms[-1].elementId
$set = @{ elementId = $rid; builtInId = -1001002; value = 'Room A'; __smoke_ok = $true } | ConvertTo-Json -Compress
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_room_param --params $set --wait-seconds 60
```

Note: Parameter keys accepted (priority): builtInId → builtInName → guid → paramName. Prefer builtInId/guid to avoid locale issues.

## Error Handling & Stall Avoidance
- Always include `params:{}` (API expects it).
- Prefer Durable (`Manuals/Scripts/send_revit_command_durable.py`). Fallback to legacy only if `/job/{id}` is unavailable.
- On 409 conflict (job in progress), retry or add `?force=1` on enqueue; better yet, serialize calls.
- If dialogs cause timeouts (e.g., duplicates), continue with the rest; merged intervals reduce repeats.

## Quick Checklist
- [ ] ExcelMCP up (`/health` ok)
- [ ] Revit MCP up (`ping_server` ok)
- [ ] Parse ok (`segmentsDetailed` > 0)
- [ ] Walls merged & created without duplication
- [ ] Rooms placed only at labels (cell center) and named correctly
- [ ] Zero‑area rooms cleaned

## Where Logs Are Saved
- `Work/<ProjectName>_<Port>/Logs/parse_run.json`
- `Work/<ProjectName>_<Port>/Logs/segments_merged.json`
- `Work/<ProjectName>_<Port>/Logs/wall_errors.json`
- `Work/<ProjectName>_<Port>/Logs/apply_summary.json`
- `Work/<ProjectName>_<Port>/Logs/rooms_from_labels_run.json`
- `Work/<ProjectName>_<Port>/Logs/rooms_zero_area_snapshot.json`
- `Work/<ProjectName>_<Port>/Logs/rooms_zero_area_delete_results.jsonl`


