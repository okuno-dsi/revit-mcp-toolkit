# move_text_note

- Category: AnnotationOps
- Purpose: Move a TextNote by offset vector (project units aware)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_text_note

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends | 0.0 |
| dy | number | no/depends | 0.0 |
| dz | number | no/depends | 0.0 |
| elementId | int | no/depends | 0 |
| unit | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_text_note",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0,
    "unit": "..."
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
    "unit": {
      "type": "string"
    },
    "elementId": {
      "type": "integer"
    },
    "dy": {
      "type": "number"
    },
    "dx": {
      "type": "number"
    },
    "dz": {
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
