# build_views_by_param_values

- Category: ViewOps
- Purpose: Build Views By Param Values in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: build_views_by_param_values

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| className | string | no/depends |  |
| detachViewTemplate | bool | no/depends | true |
| keepAnnotations | bool | no/depends | false |
| sourceViewId | int | no/depends | 0 |
| withDetailing | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "build_views_by_param_values",
  "params": {
    "className": "...",
    "detachViewTemplate": false,
    "keepAnnotations": false,
    "sourceViewId": 0,
    "withDetailing": false
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
    "sourceViewId": {
      "type": "integer"
    },
    "className": {
      "type": "string"
    },
    "keepAnnotations": {
      "type": "boolean"
    },
    "withDetailing": {
      "type": "boolean"
    },
    "detachViewTemplate": {
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
