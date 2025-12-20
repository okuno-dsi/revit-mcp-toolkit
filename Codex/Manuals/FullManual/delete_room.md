# delete_room

- Category: Room
- Purpose: Delete Room in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_room

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_room",
  "params": {
    "elementId": 0
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
