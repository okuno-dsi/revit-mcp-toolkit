# create_view_plan

- Category: ViewOps
- Purpose: Create View Plan in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_view_plan

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelName | string | no/depends |  |
| name | string | no/depends | New Plan |
| viewFamily | string | no | FloorPlan |
| view_family | string | no (compat) |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_view_plan",
  "params": {
    "levelName": "...",
    "name": "..."
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_section
- create_elevation_view
- compare_view_states
- get_views

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
    },
    "viewFamily": {
      "type": "string",
      "description": "Optional. e.g. \"FloorPlan\" (default) or \"CeilingPlan\" (RCP)."
    },
    "view_family": {
      "type": "string",
      "description": "Compatibility alias for viewFamily."
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
