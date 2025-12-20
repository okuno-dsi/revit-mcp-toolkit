# create_room_interior_elevations

- Category: ViewOps
- Purpose: Interior Elevation（展開図）を作成・情報取得

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_room_interior_elevations

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| namePrefix | string | no/depends |  |
| offsetMm | number | no/depends | 1000.0 |
| roomId | int | no/depends |  |
| scale | int | no/depends | 100 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_room_interior_elevations",
  "params": {
    "namePrefix": "...",
    "offsetMm": 0.0,
    "roomId": 0,
    "scale": 0
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "roomId": {
      "type": "integer"
    },
    "scale": {
      "type": "integer"
    },
    "offsetMm": {
      "type": "number"
    },
    "namePrefix": {
      "type": "string"
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
