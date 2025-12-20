# snapshot_view_elements

- Category: ViewOps
- Purpose: Snapshot View Elements in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: snapshot_view_elements

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| includeAnalytic | bool | no/depends | false |
| includeHidden | bool | no/depends | false |
| includeTypeParams | unknown | no/depends |  |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "snapshot_view_elements",
  "params": {
    "includeAnalytic": false,
    "includeHidden": false,
    "includeTypeParams": "...",
    "viewId": "..."
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeAnalytic": {
      "type": "boolean"
    },
    "viewId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "includeHidden": {
      "type": "boolean"
    },
    "includeTypeParams": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
