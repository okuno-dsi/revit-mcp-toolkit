# get_room_planar_centroid

- Category: Room
- Purpose: 体積重心(既存)に加えて、2D平面重心・ラベル位置を取得できるようにする

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_room_planar_centroid

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_planar_centroid",
  "params": {
    "elementId": "..."
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
