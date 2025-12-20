# move_room_boundary_line

- Category: Room
- Purpose: Room Separation Lines（部屋境界線）の作成・削除・移動・トリム・延長・クリーニング・一覧

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_room_boundary_line

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends | 0.0 |
| dy | number | no/depends | 0.0 |
| dz | number | no/depends | 0.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_room_boundary_line",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0
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
    "dz": {
      "type": "number"
    },
    "dy": {
      "type": "number"
    },
    "dx": {
      "type": "number"
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
