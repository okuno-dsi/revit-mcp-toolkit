# set_category_override

- Category: ViewOps
- Purpose: Set Category Override in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_category_override

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| applyFill | bool | no/depends | false |
| autoWorkingView | bool | no/depends | true |
| batchSize | int | no/depends | 200 |
| detachViewTemplate | bool | no/depends | false |
| maxMillisPerTx | int | no/depends | 3000 |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| transparency | int | no/depends | 40 |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_override",
  "params": {
    "applyFill": false,
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "transparency": 0,
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
    "autoWorkingView": {
      "type": "boolean"
    },
    "viewId": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "transparency": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "applyFill": {
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
