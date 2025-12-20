# delete_room_boundary_line

- Category: Room
- Purpose: Room Separation Lines（部屋境界線）の作成・削除・移動・トリム・延長・クリーニング・一覧

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_room_boundary_line
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_room_boundary_line",
  "params": {}
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
  "properties": {}
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
