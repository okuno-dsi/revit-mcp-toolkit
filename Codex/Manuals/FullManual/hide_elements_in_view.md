# hide_elements_in_view

- Category: ViewOps
- Purpose: JSON-RPC "hide_elements_in_view" の実体

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: hide_elements_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 800 |
| detachViewTemplate | bool | no/depends | false |
| elementId | int | no/depends |  |
| maxMillisPerTx | int | no/depends | 4000 |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "hide_elements_in_view",
  "params": {
    "batchSize": 0,
    "detachViewTemplate": false,
    "elementId": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
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
    "startIndex": {
      "type": "integer"
    },
    "elementId": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "viewId": {
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
