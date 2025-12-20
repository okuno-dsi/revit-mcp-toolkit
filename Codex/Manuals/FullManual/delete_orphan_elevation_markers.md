# delete_orphan_elevation_markers

- Category: ViewOps
- Purpose: Delete orphan ElevationMarker elements (elevation markers) that no longer own any live elevation views.

## Overview
When you create elevation-style views (e.g. door/window elevation views), Revit creates an `ElevationMarker` element in a plan view.
If the elevation view is later deleted, the marker can remain in the model as an “orphan marker”.

`delete_orphan_elevation_markers` scans `ElevationMarker` elements and deletes only the ones that have **zero live views** attached (all marker slots are empty or point to deleted views).

## Usage
- Method: delete_orphan_elevation_markers

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---|---|---|
| dryRun | bool | no | false | `true` returns candidates only; does not delete. |
| viewId | int | no | 0 | If set, only scans/deletes markers visible in that view. |
| batchSize | int | no | 200 | Delete chunk size per transaction (avoids long transactions). |
| limit | int | no | 0 | If > 0, deletes only the first N orphan markers found. |
| detailLimit | int | no | 200 | Max number of `orphanMarkers` detail items to return. |
| maxIds | int | no | 2000 | Truncates long `*MarkerIds` arrays in the response. |

### Example (dry run)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_orphan_elevation_markers",
  "params": {
    "dryRun": true
  }
}
```

### Example (delete, view-scoped)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_orphan_elevation_markers",
  "params": {
    "viewId": 6781048,
    "batchSize": 100
  }
}
```

## Notes
- This command does **not** delete any elevation views. It deletes markers only when **no live views** remain.
- If you need to remove a marker that still owns elevation views, delete the marker in Revit UI (or delete its remaining views first).

