# sync_view_state

- Category: ViewOps
- Purpose: Synchronize essential view properties from a source view to a target view

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: sync_view_state

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dstViewId | int | no/depends |  |
| srcViewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sync_view_state",
  "params": {
    "dstViewId": 0,
    "srcViewId": 0
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
    "srcViewId": {
      "type": "integer"
    },
    "dstViewId": {
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
