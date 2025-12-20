# create_model_arc

- Category: AnnotationOps
- Purpose: Create Model Arc in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_model_arc

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| center | object | no/depends |  |
| end | object | no/depends |  |
| endAngleDeg | number | no/depends |  |
| mid | object | no/depends |  |
| mode | string | no/depends | three_point |
| radiusMm | number | no/depends |  |
| start | object | no/depends |  |
| startAngleDeg | number | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_model_arc",
  "params": {
    "center": "...",
    "end": "...",
    "endAngleDeg": 0.0,
    "mid": "...",
    "mode": "...",
    "radiusMm": 0.0,
    "start": "...",
    "startAngleDeg": 0.0
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
    "mode": {
      "type": "string"
    },
    "mid": {
      "type": "object"
    },
    "startAngleDeg": {
      "type": "number"
    },
    "center": {
      "type": "object"
    },
    "endAngleDeg": {
      "type": "number"
    },
    "start": {
      "type": "object"
    },
    "end": {
      "type": "object"
    },
    "radiusMm": {
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
