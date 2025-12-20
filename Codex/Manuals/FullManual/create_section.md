# create_section

- Category: ViewOps
- Purpose: Create Section in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_section

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| name | string | no/depends | Section |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_section",
  "params": {
    "name": "..."
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
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
