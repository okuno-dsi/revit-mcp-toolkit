# get_tags_in_view

- Category: AnnotationOps
- Purpose: Get Tags In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_tags_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryName | string | no/depends |  |
| count | int | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_tags_in_view",
  "params": {
    "categoryName": "...",
    "count": 0,
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false
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
    "nameContains": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "count": {
      "type": "integer"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "categoryName": {
      "type": "string"
    },
    "summaryOnly": {
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
