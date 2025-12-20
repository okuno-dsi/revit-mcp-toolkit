# `replace_view_on_sheet` – Replace a view on a sheet

Purpose
- Replace an existing placed view (or schedule) on a sheet with another view, while preserving the sheet location, and optionally rotation and scale.
- Typical use: swap floor plan versions on a sheet without manually re‑placing and aligning the viewport.

Command
- Method name: `replace_view_on_sheet`
- Kind: write
- Category: Views/Graphics (ViewSheet ops)

Inputs (params)
- `viewportId` (int, optional)
  - If provided, this is the primary way to identify the existing placement.
  - May point to either a `Viewport` or a `ScheduleSheetInstance`.
- `sheetId` / `uniqueId` / `sheetNumber` (optional)
  - Used when `viewportId` is omitted. Combined with `oldViewId` / `oldViewUniqueId` to locate the placement on that sheet.
- `oldViewId` / `oldViewUniqueId` (optional)
  - Identifies the currently placed view to be replaced when `viewportId` is not given.
  - The command searches that sheet for a matching `Viewport` or `ScheduleSheetInstance`.
- `newViewId` / `newViewUniqueId` (required)
  - The view to place instead. Must not be a view template.

Location / rotation / scale options
- `keepLocation` (bool, default: `true`)
  - If `true`, uses the old placement center as the new placement location (sheet coordinates).
  - If `false`, the new location is controlled by `centerOnSheet` / `location`.
- `centerOnSheet` (bool, default: `false`)
  - Only used when `keepLocation:false`.
  - If `true`, places the new view at the sheet center (based on `SHEET_WIDTH`/`SHEET_HEIGHT` in mm).
- `location` (object, optional)
  - Only used when `keepLocation:false` and `centerOnSheet:false`.
  - `{ x: number, y: number }` in millimeters, sheet coordinate system, for the new placement center.
- `copyRotation` (bool, default: `true`)
  - If the old placement is a viewport, copy its `Viewport.Rotation` to the new viewport.
- `copyScale` (bool, default: `false`)
  - If `true`, attempts to set `newView.Scale` to `oldView.Scale` (non‑schedule views only).
  - If the API rejects the scale change, the command still succeeds and returns a `scaleWarning` message.

Behavior
- Existing placement:
  - If `viewportId` is given:
    - Resolves to a `Viewport` or `ScheduleSheetInstance` and infers the sheet and old view.
  - Otherwise:
    - Resolves the sheet from `sheetId` / `uniqueId` / `sheetNumber`.
    - Resolves the old view from `oldViewId` / `oldViewUniqueId`.
    - Searches that sheet for a `Viewport` or `ScheduleSheetInstance` that references the old view.
- Position:
  - Default: inferred from the old placement center (`Viewport.GetBoxCenter()` or schedule `Point`).
  - Can be overridden via `keepLocation:false` + `centerOnSheet` / `location`.
- Replacement:
  - Deletes the old `Viewport` / `ScheduleSheetInstance` within one transaction.
  - Creates a new placement:
    - `ScheduleSheetInstance.Create` for schedules.
    - `Viewport.Create` for other views (guarded by `Viewport.CanAddViewToSheet`).
  - Optionally copies rotation and scale as described above.

Result
- On success:
  - `{ ok:true, sheetId, oldViewId, newViewId, usedLocationFromOld, copyRotation, copyScale, scaleWarning, result }`
  - `result` is:
    - Schedules: `{ kind:"schedule", scheduleInstanceId, sheetId, viewId }`
    - Viewports: `{ kind:"viewport", viewportId, sheetId, viewId }`
- On failure:
  - `{ ok:false, msg:"..." }` (e.g., sheet/view not found, placement not found, new view cannot be placed).

Examples
- Replace a placed plan view by viewportId, keeping location, rotation, and not touching scale:

```json
{
  "method": "replace_view_on_sheet",
  "params": {
    "viewportId": 123456,
    "newViewId": 789012
  }
}
```

- Replace by sheet + old view, placing the new view at sheet center and copying scale:

```json
{
  "method": "replace_view_on_sheet",
  "params": {
    "sheetNumber": "A-101",
    "oldViewId": 234567,
    "newViewId": 890123,
    "keepLocation": false,
    "centerOnSheet": true,
    "copyScale": true
  }
}
```

