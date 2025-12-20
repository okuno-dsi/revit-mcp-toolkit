# get_dimensions_in_view

- Category: AnnotationOps
- Purpose: Get Dimensions In View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_dimensions_in_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeOrigin | bool | no/depends | true |
| includeReferences | bool | no/depends | true |
| includeValue | bool | no/depends | true |
| nameContains | string | no/depends |  |
| summaryOnly | bool | no/depends | false |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_dimensions_in_view",
  "params": {
    "includeOrigin": false,
    "includeReferences": false,
    "includeValue": false,
    "nameContains": "...",
    "summaryOnly": false,
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
    "nameContains": {
      "type": "string"
    },
    "viewId": {
      "type": "integer"
    },
    "includeReferences": {
      "type": "boolean"
    },
    "includeValue": {
      "type": "boolean"
    },
    "includeOrigin": {
      "type": "boolean"
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
