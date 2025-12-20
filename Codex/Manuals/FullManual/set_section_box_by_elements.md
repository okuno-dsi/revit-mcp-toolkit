# set_section_box_by_elements

- Category: ViewOps
- Purpose: Set Section Box By Elements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_section_box_by_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | unknown | no/depends |  |
| paddingMm | number | no/depends | 0.0 |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_section_box_by_elements",
  "params": {
    "elementIds": "...",
    "paddingMm": 0.0,
    "viewId": "..."
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
    "elementIds": {
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
    "paddingMm": {
      "type": "number"
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
