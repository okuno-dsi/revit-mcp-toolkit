# audit_hidden_in_view

- Category: ViewOps
- Purpose: 「非表示要素の一時表示（電球ボタン）」相当の監査を最小構成で返す。

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: audit_hidden_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeCategoryStates | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "audit_hidden_in_view",
  "params": {
    "includeCategoryStates": false
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
    "includeCategoryStates": {
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
