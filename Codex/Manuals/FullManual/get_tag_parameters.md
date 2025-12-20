# get_tag_parameters

- Category: AnnotationOps
- Purpose: Get Tag Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_tag_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| tagId | int | no/depends | 0 |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_tag_parameters",
  "params": {
    "count": 0,
    "namesOnly": false,
    "skip": 0,
    "tagId": 0,
    "uniqueId": "..."
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
    "skip": {
      "type": "integer"
    },
    "tagId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "count": {
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
