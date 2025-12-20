# get_room_boundary

- Category: Room
- Purpose: Get Room Boundary in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_room_boundary

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| boundaryLocation | string | no | `Finish` |
| count | int | no/depends |  |
| elementId | unknown | no/depends |  |
| skip | int | no/depends | 0 |

`boundaryLocation` controls `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation`:
- `Finish` (inner finish face / 内法)
- `Center` (wall centerline)
- `CoreCenter` (wall core centerline / 躯体芯)
- `CoreBoundary` (wall core boundary face)

Notes:
- This is **A)** “computed boundary” based on `Room.GetBoundarySegments(...)`.
- For **B)** actual *Room Separation Lines* (model curve elements), use `get_room_boundary_lines_in_view` and related `create_*_room_boundary_line` commands.
- If the prompt says “room boundary line” and it’s unclear which meaning is intended, ask the user to choose A) vs B) (see `Manuals/FullManual/spatial_boundary_location.md`).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundary",
  "params": {
    "boundaryLocation": "CoreCenter",
    "count": 0,
    "elementId": "...",
    "skip": 0
  }
}
```

## Related
- find_room_placeable_regions
- summarize_rooms_by_level
- validate_create_room
- get_rooms
- get_room_params
- set_room_param
- create_room
- delete_room

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "skip": {
      "type": "integer"
    },
    "elementId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "count": {
      "type": "integer"
    }
  }
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
