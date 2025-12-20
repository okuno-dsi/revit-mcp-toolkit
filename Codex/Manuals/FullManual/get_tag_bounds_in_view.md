# get_tag_bounds_in_view

- Category: AnnotationOps
- Purpose: Get Tag Bounds In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_tag_bounds_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| inflateMm | number | no/depends | 0.0 |
| tagId | unknown | no/depends |  |
| uniqueId | unknown | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_tag_bounds_in_view",
  "params": {
    "inflateMm": 0.0,
    "tagId": "...",
    "uniqueId": "...",
    "viewId": 0
  }
}
```

## Related
- get_detail_line_styles
- get_detail_lines_in_view
- create_detail_line
- create_detail_arc
- move_detail_line
- rotate_detail_line
- delete_detail_line
- get_line_styles

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "tagId": {
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
    "uniqueId": {
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
    "viewId": {
      "type": "integer"
    },
    "inflateMm": {
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
