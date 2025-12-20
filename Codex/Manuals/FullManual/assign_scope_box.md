# assign_scope_box

- Category: ViewOps
- Purpose: Scope Box（スコープボックス）関連コマンド群を「1ファイル」に集約

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: assign_scope_box

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| scopeBoxId | int | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "assign_scope_box",
  "params": {
    "scopeBoxId": 0,
    "viewId": 0
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
    "viewId": {
      "type": "integer"
    },
    "scopeBoxId": {
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
