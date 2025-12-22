# viewport.move_to_sheet_center

- Category: ViewSheet Ops
- Purpose: Move a viewport to the center of its sheet (supports dryRun).

## Overview
High-level viewport command (Step 5).

- Canonical: `viewport.move_to_sheet_center`
- Legacy alias: `viewport_move_to_sheet_center`

## Usage
- Method: `viewport.move_to_sheet_center`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewportId | integer | yes* |  |
| sheet | object | no (fallback) |  |
| viewId | integer | no (fallback) |  |
| dryRun | boolean | no | false |

Notes:
- If `viewportId` is omitted, you can pass `sheet` + `viewId` to locate the viewport on that sheet.
- `sheet`: `{ id } | { number } | { name } | { uniqueId }` (same as `sheet.place_view_auto`)
- Sheet center is computed from `SHEET_WIDTH/HEIGHT` when available; otherwise it falls back to the titleblock bounding box center.

### Example Request (dryRun)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "viewport.move_to_sheet_center",
  "params": {
    "viewportId": 123456,
    "dryRun": true
  }
}
```

## Related
- sheet.place_view_auto
- place_view_on_sheet
- get_view_placements
