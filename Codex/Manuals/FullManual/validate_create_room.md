# validate_create_room

- Category: Room
- Purpose: Validate Create Room in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: validate_create_room
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "validate_create_room",
  "params": {}
}
```

## Related
- find_room_placeable_regions
- summarize_rooms_by_level
- get_rooms
- get_room_params
- set_room_param
- get_room_boundary
- create_room
- delete_room

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
