# create_room_masses

- Category: Room
- Purpose: Create DirectShape “Mass” volumes from Room boundaries (bulk, safe).

## Overview
Creates a DirectShape solid (extrusion) for each target Room, using the Room boundary loop and a computed height.

Safety/performance notes:
- The command enforces `maxRooms` to avoid overload.
- Rooms that fail boundary/shape creation are skipped and recorded in `issues.itemErrors[]`.

## Usage
- Method: `create_room_masses`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| roomIds | int[] | no |  |
| viewId | int | no | 0 |
| maxRooms | int | no | 200 |
| heightMode | string | no | bbox |
| fixedHeightMm | number | no | 2800.0 |
| useMassCategory | bool | no | true |

- `heightMode`:
  - `"bbox"`: use Room bounding box height (fallback to `fixedHeightMm` if 0).
  - `"fixed"`: always use `fixedHeightMm`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room_masses",
  "params": {
    "viewId": 1332698,
    "maxRooms": 200,
    "heightMode": "bbox",
    "fixedHeightMm": 2800.0,
    "useMassCategory": true
  }
}
```

## Related
- get_rooms
- get_room_boundary
- get_room_perimeter_with_columns

