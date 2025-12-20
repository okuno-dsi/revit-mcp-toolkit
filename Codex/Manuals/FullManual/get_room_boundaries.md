# get_room_boundaries

- Category: Room
- Purpose: Get **calculated** boundaries for multiple Rooms (mm coordinates).

## Overview
`get_room_boundaries` is a read-only bulk command to fetch room boundary loops (and optional island loops) in one call.
It uses `Room.GetBoundarySegments(...)` (A: calculated boundary). See `Manuals/FullManual/spatial_boundary_location.md`.

## Usage
- Method: `get_room_boundaries`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---:|:---:|---|---|
| roomIds | int[] | no |  | Target Room `elementId` list (takes priority if provided). |
| viewId | int | no | 0 | If `roomIds` is omitted: target Rooms visible in this view. |
| includeIslands | bool | no | true | Include island loops when available. |
| boundaryLocation | string | no | `Finish` | `Finish` / `Center` / `CoreCenter` / `CoreBoundary` |

### Example Request (Rooms in view)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundaries",
  "params": {
    "viewId": 1332698,
    "includeIslands": true,
    "boundaryLocation": "Finish"
  }
}
```

## Notes
- Response includes `boundaryLocation` at the top-level.
- If a Room is unplaced/not enclosed, it is reported in `issues.itemErrors[]` and other Rooms still succeed.

