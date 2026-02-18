# `replace_view_on_sheet` – Replace a view on a sheet

Purpose
- Replace an existing placed view (or schedule) on a sheet with another view, while preserving the sheet location, and optionally rotation and scale.
- Typical use: swap floor plan versions on a sheet without manually re‑placing and aligning the viewport.
- Supports scale-different alignment by anchoring to a model grid intersection and aligning in sheet space.

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

Scale-aware grid-anchor alignment options
- `alignByGridIntersection` (object, optional)
  - `{ referenceViewportId:int, gridA:string, gridB:string, enabled?:bool }`
  - `referenceViewportId`: existing viewport on the same sheet used as the alignment reference.
  - `gridA` / `gridB`: two grid names whose intersection is used as anchor in model space.
  - `enabled`: optional explicit switch (`true`/`false`).
- Compatibility shortcut fields are also accepted at top level:
  - `referenceViewportId`, `gridA`, `gridB`

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
- Grid-anchor alignment (viewport only):
  - After creating the new viewport, the command can align by the same model anchor across viewports:
    - Resolve model anchor from `gridA` x `gridB`.
    - Project anchor to sheet for both `referenceViewportId` and new viewport.
    - Move new viewport by sheet delta so both anchor positions match.
  - This makes alignment robust even when view scales are different.
- Replacement:
  - Deletes the old `Viewport` / `ScheduleSheetInstance` within one transaction.
  - Creates a new placement:
    - `ScheduleSheetInstance.Create` for schedules.
    - `Viewport.Create` for other views (guarded by `Viewport.CanAddViewToSheet`).
  - Optionally copies rotation and scale as described above.

Result
- On success:
  - `{ ok:true, sheetId, oldViewId, newViewId, usedLocationFromOld, copyRotation, copyScale, scaleWarning, alignWarning, alignment, result }`
  - `alignWarning`: warning text when anchor-align was requested but could not be applied.
  - `alignment`: details when applied:
    - `anchorModelMm`, `deltaSheetMm`, `newViewportCenterBeforeMm`, `newViewportCenterAfterMm`
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

- Replace and align to a grid intersection across different scales:

```json
{
  "method": "replace_view_on_sheet",
  "params": {
    "viewportId": 5785889,
    "newViewId": 4383347,
    "alignByGridIntersection": {
      "referenceViewportId": 5785884,
      "gridA": "X1",
      "gridB": "Y3",
      "enabled": true
    }
  }
}
```
