# delete_detail_line

- Category: AnnotationOps
- Purpose: Delete Detail Line(s) in a view.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Canonical method: `view.delete_detail_line`
- Deprecated alias: `delete_detail_line`

### Parameters
Provide **one of** the following:

| Name | Type | Required | Notes |
|---|---|---:|---|
| elementId | int | yes (one of) | Single detail line element id |
| elementIds | int[] | yes (one of) | Bulk delete (recommended) |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.delete_detail_line",
  "params": {
    "elementIds": [123, 456, 789]
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
- get_line_styles
- set_detail_line_style

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elementId": { "type": "integer" },
    "elementIds": { "type": "array", "items": { "type": "integer" } }
  }
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "deletedCount": { "type": "integer" },
    "requested": { "type": "integer" },
    "skipped": { "type": "array" }
  },
  "additionalProperties": true
}
```
