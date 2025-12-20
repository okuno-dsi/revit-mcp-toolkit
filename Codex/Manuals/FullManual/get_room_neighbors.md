# get_room_neighbors

- Category: Room
- Purpose: Roomの外周から隣接する他Roomを列挙（共有長さmmも集計）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_room_neighbors

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| boundaryLocation | string | no | `Finish` |
| roomId | unknown | no/depends |  |

`boundaryLocation` controls `SpatialElementBoundaryOptions.SpatialElementBoundaryLocation` (same values as `get_room_boundary`).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_neighbors",
  "params": {
    "boundaryLocation": "CoreCenter",
    "roomId": "..."
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
- get_room_boundary
- create_room

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "roomId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
