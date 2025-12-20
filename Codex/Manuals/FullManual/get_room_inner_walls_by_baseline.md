# get_room_inner_walls_by_baseline

- Category: Room
- Purpose: Find inner walls of a room by sampling the wall baseline inside the room volume.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in.  
Given a `roomId` and optional parameters, it:

- Collects walls on the same level as the room (or spanning across that level).
- For each candidate wall, samples two points on its `LocationCurve` at fractions `t1` and `t2` (e.g. 0.33 and 0.67).
- Evaluates those points at the room’s mid-height and checks `Room.IsPointInRoom`.

If both sampled points are inside the room, the wall is returned as an “inner wall”.  
This also detects walls that are “floating” in 3D (e.g. dropped ceilings / soffit walls) as long as their XY location lies within the room projection.

## Usage
- Method: get_room_inner_walls_by_baseline

### Parameters
| Name | Type | Required | Default | Description |
|---|---|---|---|---|
| roomId | int | yes | – | ElementId of the target Room |
| t1 | double | no | 0.33 | First sample fraction on the wall baseline (0.0–1.0) |
| t2 | double | no | 0.67 | Second sample fraction on the wall baseline (0.0–1.0) |
| searchRadiusMm | double | no | null | Optional XY radius (mm) around the room center to limit candidate walls |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_inner_walls_by_baseline",
  "params": {
    "roomId": 6808111,
    "t1": 0.33,
    "t2": 0.67
  }
}
```

### Example Response
```json
{
  "ok": true,
  "result": {
    "roomId": 6808111,
    "levelId": 11815,
    "levelName": "2FL",
    "t1": 0.33,
    "t2": 0.67,
    "searchRadiusMm": null,
    "wallCount": 11,
    "walls": [
      {
        "wallId": 8362180,
        "uniqueId": "...",
        "typeId": 8338189,
        "typeName": "(内壁)W5",
        "levelId": 11815,
        "levelName": "2FL",
        "sample": {
          "t1": 0.33,
          "t2": 0.67,
          "p1": { "x": 21138.1, "y": -205.0, "z": 5319.2 },
          "p2": { "x": 22861.9, "y": -205.0, "z": 5319.2 }
        }
      }
    ]
  }
}
```

- `walls[*].sample.p1/p2` contain the sample points (mm) used for the in-room test.
- You can use `wallId` with `lookup_element`, `get_wall_parameters`, etc. to retrieve more details.

## Related
- get_rooms
- get_room_params
- get_room_boundary
- get_room_boundary_walls
- get_room_perimeter_with_columns_and_walls

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "roomId": {
      "type": "integer"
    },
    "t1": {
      "type": "number"
    },
    "t2": {
      "type": "number"
    },
    "searchRadiusMm": {
      "type": "number"
    }
  },
  "required": ["roomId"]
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {},
  "additionalProperties": true
}
```

