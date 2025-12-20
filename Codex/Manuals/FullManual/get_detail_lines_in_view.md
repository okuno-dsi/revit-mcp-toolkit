# get_detail_lines_in_view

- Category: AnnotationOps
- Purpose: Get Detail Lines In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_detail_lines_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeEndpoints | bool | no/depends | true |
| styleNameContains | string | no/depends |  |
| summaryOnly | bool | no/depends | false |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_detail_lines_in_view",
  "params": {
    "includeEndpoints": false,
    "styleNameContains": "...",
    "summaryOnly": false,
    "viewId": 0
  }
}
```

## Related
- get_detail_line_styles
- create_detail_line
- create_detail_arc
- move_detail_line
- rotate_detail_line
- delete_detail_line
- get_line_styles
- set_detail_line_style

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeEndpoints": {
      "type": "boolean"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "styleNameContains": {
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
