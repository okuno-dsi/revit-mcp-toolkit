# clean_room_boundaries

- Category: Room
- Purpose: Room Separation Lines（部屋境界線）の作成・削除・移動・トリム・延長・クリーニング・一覧

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: clean_room_boundaries

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| deleteIsolated | bool | no/depends | false |
| extendToleranceMm | number | no/depends | 50.0 |
| mergeToleranceMm | number | no/depends | 5.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clean_room_boundaries",
  "params": {
    "deleteIsolated": false,
    "extendToleranceMm": 0.0,
    "mergeToleranceMm": 0.0
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
    "mergeToleranceMm": {
      "type": "number"
    },
    "deleteIsolated": {
      "type": "boolean"
    },
    "extendToleranceMm": {
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
