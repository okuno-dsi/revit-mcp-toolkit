# update_text_note_parameter

- Category: AnnotationOps
- Purpose: Update TextNote / TextNoteType parameter with project units

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_text_note_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| applyToType | bool | no/depends | false |
| elementId | int | no/depends | 0 |
| paramName | string | no/depends |  |
| unit | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_text_note_parameter",
  "params": {
    "applyToType": false,
    "elementId": 0,
    "paramName": "...",
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
    "paramName": {
      "type": "string"
    },
    "applyToType": {
      "type": "boolean"
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
