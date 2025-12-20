# set_category_visibility

- Category: ViewOps
- Purpose: JSON-RPC "set_category_visibility" for a view

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_category_visibility

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryIds | unknown | no/depends |  |
| viewId | int | no/depends |  |
| visible | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_visibility",
  "params": {
    "categoryIds": "...",
    "viewId": 0,
    "visible": false
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
    "categoryIds": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "visible": {
      "type": "boolean"
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
