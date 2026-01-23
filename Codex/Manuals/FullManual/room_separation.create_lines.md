# room_separation.create_lines

- Category: Room
- Purpose: Create Room Separation Lines (Room Boundary Lines) from line segments in a plan view.

## Overview
Creates one or more **Room Separation Lines** in a plan view (Floor Plan / Ceiling Plan).

Use this when you want to define room boundaries **without modeling walls**, or when you want to temporarily control room/area separation with lines.

## Usage
- Method: `room_separation.create_lines`
- Transaction: Write

### Parameters
| Name | Type | Required | Default |
|---|---|---:|---|
| viewId | int | no | ActiveView |
| unit | string | no | `"mm"` |
| snapZToViewLevel | bool | no | `true` |
| segments | object[] | yes (recommended) |  |
| start | object | legacy |  |
| end | object | legacy |  |

Point format (coordinates):
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

Segment format:
```json
{ "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 1000, "y": 0, "z": 0 } }
```

Notes:
- `segments[]` is preferred. If `segments[]` is present, `start/end` are ignored.
- `unit` supports: `"mm"` / `"m"` / `"ft"`. (Default: `"mm"`.)
- When `snapZToViewLevel=true`, the command snaps both endpoints to the **view level elevation** (PathOfTravel-style behavior).
- Zero-length segments are skipped and reported in `result.skipped[]`.

### Example Request (segments)
```json
{
  "jsonrpc": "2.0",
  "id": "rs-1",
  "method": "room_separation.create_lines",
  "params": {
    "viewId": 123456,
    "unit": "mm",
    "snapZToViewLevel": true,
    "segments": [
      { "start": { "x": 0, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 0, "z": 0 } },
      { "start": { "x": 5000, "y": 0, "z": 0 }, "end": { "x": 5000, "y": 3000, "z": 0 } }
    ]
  }
}
```

## Result
- `ok`: boolean
- `result.created`: int[] created Room Separation Line ids
- `result.count`: int
- `result.skipped[]`: optional list of skipped segments: `{ index, reason }`

## Related
- `create_room_boundary_line` (deprecated alias of this command)

