# create_walkthrough

- Category: ViewOps
- Purpose: Create Walkthrough in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_walkthrough

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| frameCount | int | no/depends | 300 |
| name | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_walkthrough",
  "params": {
    "frameCount": 0,
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
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "frameCount": {
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
