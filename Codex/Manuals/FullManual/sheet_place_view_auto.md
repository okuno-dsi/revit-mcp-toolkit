# sheet.place_view_auto

- Category: ViewSheet Ops
- Purpose: Place a view on a sheet, and automatically handle “already placed” by duplicating the view if needed.

## Overview
High-level intent command (Step 5).

- Canonical: `sheet.place_view_auto`
- Legacy alias: `place_view_on_sheet_auto`

Behavior:
- Regular views → creates a `Viewport`
- Schedules (`ViewSchedule`) → creates a `ScheduleSheetInstance`

## Usage
- Method: `sheet.place_view_auto`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| sheet | object | no* | (active sheet view) |
| view | object | yes* |  |
| placement | object | no | `{ "mode": "center" }` |
| ifAlreadyPlaced | string | no | `duplicate_dependent` |
| dryRun | boolean | no | false |
| sheetId | integer | no (legacy) |  |
| sheetNumber | string | no (legacy) |  |
| uniqueId | string | no (legacy) |  |
| viewId | integer | no (legacy) |  |
| viewUniqueId | string | no (legacy) |  |
| centerOnSheet | boolean | no (legacy) | false |
| location | object | no (legacy) |  |

Notes:
- `sheet`: `{ id } | { number } | { name } | { uniqueId }`
- `view`: `{ id } | { name } | { uniqueId }`
- `placement.mode`: `center` or `point`
- `placement.pointMm`: `{ x, y }` in mm when `mode="point"`
- When placing to the sheet center, the add-in uses `SHEET_WIDTH/HEIGHT` if available; if they are 0/unknown, it falls back to the titleblock bounding box center.
- If `placement` is omitted, it defaults to sheet center (returned as a warning).
- `ifAlreadyPlaced`:
  - `error`: returns `CONSTRAINT_VIEW_ALREADY_PLACED`
  - `duplicate`: duplicates the view (Duplicate) then places it
  - `duplicate_dependent`: tries dependent duplication first (AsDependent); if unsupported, falls back to Duplicate (warning)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.place_view_auto",
  "params": {
    "sheet": { "number": "A-101" },
    "view": { "name": "Level 1 - Plan" },
    "placement": { "mode": "center" },
    "ifAlreadyPlaced": "duplicate_dependent",
    "dryRun": false
  }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "data": {
    "kind": "viewport",
    "sheetId": 111,
    "sourceViewId": 222,
    "viewId": 333,
    "createdViewId": 333,
    "viewportId": 444
  }
}
```

## Related
- place_view_on_sheet
- replace_view_on_sheet
- get_view_placements
- viewport.move_to_sheet_center
- sheet.remove_titleblocks_auto
- help.describe_command
