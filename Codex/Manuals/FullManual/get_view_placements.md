# get_view_placements

- Category: ViewOps
- Purpose: Get View Placements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_view_placements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no/depends | 0 |
| viewUniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_view_placements",
  "params": {
    "viewId": 0,
    "viewUniqueId": "..."
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
    "viewUniqueId": {
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
