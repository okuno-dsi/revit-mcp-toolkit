# update_tag_parameter

- Category: AnnotationOps
- Purpose: Update Tag Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_tag_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| paramName | string | no/depends |  |
| tagId | int | no/depends | 0 |
| uniqueId | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_tag_parameter",
  "params": {
    "paramName": "...",
    "tagId": 0,
    "uniqueId": "...",
    "value": "..."
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
    "paramName": {
      "type": "string"
    },
    "tagId": {
      "type": "integer"
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
    },
    "uniqueId": {
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
