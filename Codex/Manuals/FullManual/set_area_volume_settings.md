# set_area_volume_settings

- Category: Room
- Purpose: Area/Volume 計算設定の参照・更新（ComputeVolumes）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_area_volume_settings

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| computeVolumes | bool | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_area_volume_settings",
  "params": {
    "computeVolumes": false
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
    "computeVolumes": {
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
