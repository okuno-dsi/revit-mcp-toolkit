# get_text_notes_in_view

- Category: AnnotationOps
- Purpose: List TextNotes in a view

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_text_notes_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeBBox | bool | no/depends | false |
| summaryOnly | bool | no/depends | false |
| textContains | string | no/depends |  |
| textRegex | string | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_text_notes_in_view",
  "params": {
    "includeBBox": false,
    "summaryOnly": false,
    "textContains": "...",
    "textRegex": "...",
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
    "textContains": {
      "type": "string"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "textRegex": {
      "type": "string"
    },
    "includeBBox": {
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
