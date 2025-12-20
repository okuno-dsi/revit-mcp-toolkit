# rotate_detail_line

- Category: AnnotationOps
- Purpose: Rotate Detail Line in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: rotate_detail_line

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| angleDeg | number | no/depends |  |
| elementId | int | no/depends |  |
| origin | object | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rotate_detail_line",
  "params": {
    "angleDeg": 0.0,
    "elementId": 0,
    "origin": "..."
  }
}
```

## Related
- get_detail_line_styles
- get_detail_lines_in_view
- create_detail_line
- create_detail_arc
- move_detail_line
- delete_detail_line
- get_line_styles
- set_detail_line_style

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "origin": {
      "type": "object"
    },
    "elementId": {
      "type": "integer"
    },
    "angleDeg": {
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
