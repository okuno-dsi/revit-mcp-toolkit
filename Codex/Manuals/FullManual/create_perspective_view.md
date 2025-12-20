# create_perspective_view

- Category: ViewOps
- Purpose: Create Perspective View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_perspective_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| name | string | no/depends |  |
| templateViewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_perspective_view",
  "params": {
    "name": "...",
    "templateViewId": 0
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
    "templateViewId": {
      "type": "integer"
    },
    "name": {
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
