# create_elevation_view

- Category: ViewOps
- Purpose: Create Elevation View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_elevation_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelName | string | no/depends |  |
| name | string | no/depends | 新しい軸組図 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_elevation_view",
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
- create_view_plan
- create_section
- compare_view_states
- get_views

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelName": {
      "type": "string"
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
