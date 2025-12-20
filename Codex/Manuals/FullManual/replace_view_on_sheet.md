# replace_view_on_sheet

- Category: ViewOps / ViewSheet
- Purpose: Replace an already-placed view (or schedule) on a sheet with another view, optionally keeping location/rotation/scale.

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

## Related
- place_view_on_sheet
- remove_view_from_sheet
- get_view_placements

