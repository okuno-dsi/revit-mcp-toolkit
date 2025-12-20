# get_dimension_types

- Category: AnnotationOps
- Purpose: Get Dimension Types in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_dimension_types

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| nameContains | string | no/depends |  |
| summaryOnly | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_dimension_types",
  "params": {
    "nameContains": "...",
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
    "summaryOnly": {
      "type": "boolean"
    },
    "nameContains": {
      "type": "string"
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
