# get_room_perimeter_with_columns

- Category: Room
- Purpose: Compute the perimeter of a room, temporarily treating specified (or auto-detected) columns as room-bounding, and optionally return boundary segment coordinates in millimeters.

## Overview
This command is executed via JSON-RPC against the Revit MCP add-in.
It can temporarily toggle given columns to be room-bounding, regenerate the model, then compute the room perimeter using either the built-in `ROOM_PERIMETER` parameter or boundary segments from `Room.GetBoundarySegments`.
All temporary changes are wrapped in a `TransactionGroup.RollBack()`, so the model is not modified.
Optionally, you can also request the boundary loops and line segment coordinates for later processing (e.g. finding walls near the perimeter).

## Usage
- Method: `get_room_perimeter_with_columns`

### Parameters
| Name                             | Type          | Required | Default   | Description |
|----------------------------------|---------------|---------:|-----------|-------------|
| `roomId`                         | integer       | yes      | ?         | ElementId of the target `Room`. |
| `columnIds`                      | integer[]     | no       | `[]`      | ElementIds of columns to temporarily set as room-bounding. |
| `autoDetectColumnsInRoom`        | boolean       | no       | `false`   | If `true`, automatically detects columns (architectural / structural) that intersect the room and merges them into `columnIds`. |
| `searchMarginMm`                 | number        | no       | `1000.0`  | Search margin (mm) around the room's bounding box when `autoDetectColumnsInRoom` is `true`. |
| `includeIslands`                 | boolean       | no       | `true`    | If `true`, includes all boundary loops (including islands); if `false`, only the main outer loop is used for perimeter and segment output. |
| `includeSegments`                | boolean       | no       | `false`   | If `true`, returns boundary loops and line segment coordinates as `loops` in the result. |
| `boundaryLocation`               | string        | no       | `"Finish"` | Boundary location used by `SpatialElementBoundaryOptions`: `"Finish"`, `"Center"`, `"CoreCenter"`, `"CoreFace"`. |
| `useBuiltInPerimeterIfAvailable` | boolean       | no       | `true`    | If `true`, uses `ROOM_PERIMETER` when available; otherwise falls back to curve-based perimeter from boundary segments. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_perimeter_with_columns",
  "params": {
    "roomId": 123456,
    "columnIds": [234567, 234568],
    "autoDetectColumnsInRoom": true,
    "searchMarginMm": 1000.0,
    "includeIslands": true,
    "includeSegments": true,
    "boundaryLocation": "Finish",
    "useBuiltInPerimeterIfAvailable": true
  }
}
```

### Example Result
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "roomId": 123456,
    "perimeterMm": 24850.0,
    "loops": [
      {
        "loopIndex": 0,
        "segments": [
          {
            "start": { "x": 1000.0, "y": 2000.0, "z": 0.0 },
            "end":   { "x": 5000.0, "y": 2000.0, "z": 0.0 }
          }
          // ... other segments
        ]
      }
      // ... other loops when includeIslands = true
    ],
    "units": {
      "Length": "mm"
    },
    "basis": {
      "includeIslands": true,
      "boundaryLocation": "Finish",
      "useBuiltInPerimeterIfAvailable": true,
      "autoDetectColumnsInRoom": true,
      "searchMarginMm": 1000.0,
      "autoDetectedColumnIds": [345001, 345002],
      "toggledColumnIds": [234567, 234568, 345001, 345002]
    }
  }
}
```

## Related
- get_room_boundary
- get_room_boundary_walls
- get_room_centroid
- get_room_planar_centroid
- summarize_rooms_by_level

