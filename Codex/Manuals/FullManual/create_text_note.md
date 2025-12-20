# create_text_note

- Category: AnnotationOps
- Purpose: Create a TextNote at given position (project units aware)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_text_note

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 50 |
| maxMillisPerTx | int | no/depends | 100 |
| refreshView | bool | no/depends | false |
| startIndex | int | no/depends | 0 |
| text | string | no/depends |  |
| typeName | string | no/depends |  |
| unit | string | no/depends |  |
| viewId | int | no/depends |  |
| x | number | no/depends | 0.0 |
| y | number | no/depends | 0.0 |
| z | number | no/depends | 0.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_text_note",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "text": "...",
    "typeName": "...",
    "unit": "...",
    "viewId": 0,
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
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
    "startIndex": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "z": {
      "type": "number"
    },
    "refreshView": {
      "type": "boolean"
    },
    "x": {
      "type": "number"
    },
    "text": {
      "type": "string"
    },
    "unit": {
      "type": "string"
    },
    "y": {
      "type": "number"
    },
    "batchSize": {
      "type": "integer"
    },
    "typeName": {
      "type": "string"
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
