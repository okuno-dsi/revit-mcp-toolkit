# get_room_params

- Category: Room
- Purpose: Get Room Params in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_room_params

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| roomId | unknown | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_params",
  "params": {
    "count": 0,
    "roomId": "...",
    "skip": 0
  }
}
```

## Related
- find_room_placeable_regions
- summarize_rooms_by_level
- validate_create_room
- get_rooms
- set_room_param
- get_room_boundary
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
