# get_rooms

- Category: Room
- Purpose: Get Rooms in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_rooms

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| compat | bool | no/depends | false |
| count | int | no/depends |  |
| level | string | no/depends |  |
| nameContains | string | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_rooms",
  "params": {
    "compat": false,
    "count": 0,
    "level": "...",
    "nameContains": "...",
    "skip": 0
  }
}
```

## Related
- find_room_placeable_regions
- summarize_rooms_by_level
- validate_create_room
- get_room_params
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
    "count": {
      "type": "integer"
    },
    "level": {
      "type": "string"
    },
    "nameContains": {
      "type": "string"
    },
    "compat": {
      "type": "boolean"
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

