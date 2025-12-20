# create_room

- Category: Room
- Purpose: Create Room in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_room

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoTag | bool | no/depends | true |
| checkExisting | bool | no/depends | true |
| levelId | int | no/depends |  |
| levelName | string | no/depends |  |
| minAreaMm2 | number | no/depends | 1 |
| strictEnclosure | bool | no/depends | true |
| tagTypeId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room",
  "params": {
    "autoTag": false,
    "checkExisting": false,
    "levelId": 0,
    "levelName": "...",
    "minAreaMm2": 0.0,
    "strictEnclosure": false,
    "tagTypeId": 0
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
- delete_room

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "checkExisting": {
      "type": "boolean"
    },
    "levelId": {
      "type": "integer"
    },
    "tagTypeId": {
      "type": "integer"
    },
    "autoTag": {
      "type": "boolean"
    },
    "minAreaMm2": {
      "type": "number"
    },
    "levelName": {
      "type": "string"
    },
    "strictEnclosure": {
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
