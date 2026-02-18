# replace_view_on_sheet

- Category: ViewOps / ViewSheet
- Purpose: Replace an already-placed view (or schedule) on a sheet with another view, optionally keeping location/rotation/scale.
- Purpose: Replace an already-placed view (or schedule) on a sheet with another view, optionally keeping location/rotation/scale, and optionally aligning by a grid-intersection anchor across scale-different viewports.

## Overview
This command finds an existing placement on a sheet (Viewport or ScheduleSheetInstance) and replaces it with a different view.

## Usage
- Method: `replace_view_on_sheet`

### Parameters (summary)
Placement to replace:
- `viewportId` (int, optional): if provided, used as the primary locator.
- Otherwise: specify sheet (`sheetId` / `uniqueId` / `sheetNumber`) and old view (`oldViewId` / `oldViewUniqueId`).

New view:
- `newViewId` (int) or `newViewUniqueId` (string) is required.

Options:
- `keepLocation` (bool, default `true`)
- `centerOnSheet` (bool, default `false`, only when `keepLocation:false`)
- `location` (object `{x,y}` in mm, required when `keepLocation:false` and `centerOnSheet:false`)
- `copyRotation` (bool, default `true`)
- `copyScale` (bool, default `false`)
- `alignByGridIntersection` (object, optional)
  - `{ referenceViewportId:int, gridA:string, gridB:string, enabled?:bool }`
  - Uses model grid intersection (`gridA` x `gridB`) as anchor and adjusts new viewport center in sheet coordinates.
  - Helpful when source/target views have different scales.

### Example Request (replace by viewportId)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "replace_view_on_sheet",
  "params": {
    "viewportId": 123456,
    "newViewId": 789012
  }
}
```

### Example Request (scale-different alignment by grid anchor)
```json
{
  "jsonrpc": "2.0",
  "id": 2,
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

### Response additions
- `alignWarning`: warning when anchor-alignment is requested but cannot be applied.
- `alignment`: details when applied (`anchorModelMm`, `deltaSheetMm`, before/after center in mm).

## Related
- place_view_on_sheet
- remove_view_from_sheet
- get_view_placements
