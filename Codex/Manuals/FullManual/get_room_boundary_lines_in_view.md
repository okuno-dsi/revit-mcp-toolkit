# get_room_boundary_lines_in_view

- Category: Room
- Purpose: Room Separation Lines（部屋境界線）の作成・削除・移動・トリム・延長・クリーニング・一覧

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

Notes:
- This is **B)** *Room Separation Lines* (model curve elements). It does **not** return computed room boundaries.
- If no Room Separation Lines were drawn in the project, the result can be empty even if rooms exist.
- If the prompt says “room boundary line” and it’s unclear which meaning is intended, ask the user to choose A) vs B) (see `Manuals/FullManual/spatial_boundary_location.md`).

## Usage
- Method: get_room_boundary_lines_in_view
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundary_lines_in_view",
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
