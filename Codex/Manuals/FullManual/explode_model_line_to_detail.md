# explode_model_line_to_detail

- Category: AnnotationOps
- Purpose: Explode Model Line To Detail in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: explode_model_line_to_detail

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "explode_model_line_to_detail",
  "params": {
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
