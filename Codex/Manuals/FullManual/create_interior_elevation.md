# create_interior_elevation

- Category: ViewOps
- Purpose: Interior Elevation（展開図）を作成・情報取得

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_interior_elevation

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| facing | string | no/depends |  |
| hostViewId | int | no/depends | 0 |
| name | string | no/depends |  |
| scale | int | no/depends | 100 |
| slotIndex | int | no/depends | -1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_interior_elevation",
  "params": {
    "facing": "...",
    "hostViewId": 0,
    "name": "...",
    "scale": 0,
    "slotIndex": 0
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
    "hostViewId": {
      "type": "integer"
    },
    "scale": {
      "type": "integer"
    },
    "slotIndex": {
      "type": "integer"
    },
    "name": {
      "type": "string"
    },
    "facing": {
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
