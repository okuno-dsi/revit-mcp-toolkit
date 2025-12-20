# set_view_parameter

- Category: ViewOps
- Purpose: View Delete / View Template Assign-Clear / Save as Template / Rename Template

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_view_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detachViewTemplate | bool | no/depends | false |
| paramName | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_parameter",
  "params": {
    "detachViewTemplate": false,
    "paramName": "...",
    "value": "..."
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
    "detachViewTemplate": {
      "type": "boolean"
    },
    "paramName": {
      "type": "string"
    },
    "value": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
