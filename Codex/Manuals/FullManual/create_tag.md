# create_tag

- Category: AnnotationOps
- Purpose: Create Tag in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_tag

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| addLeader | bool | no/depends | true |
| orientation | string | no/depends | Horizontal |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_tag",
  "params": {
    "addLeader": false,
    "orientation": "..."
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
    "orientation": {
      "type": "string"
    },
    "addLeader": {
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
