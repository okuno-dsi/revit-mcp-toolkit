# delete_view

- Category: ViewOps
- Purpose: View Delete / View Template Assign-Clear / Save as Template / Rename Template

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| closeOpenUiViews | bool | no/depends | true |
| fast | bool | no/depends | true |
| refreshView | bool | no/depends | false |
| useTempDraftingView | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_view",
  "params": {
    "closeOpenUiViews": false,
    "fast": false,
    "refreshView": false,
    "useTempDraftingView": false
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
    "useTempDraftingView": {
      "type": "boolean"
    },
    "refreshView": {
      "type": "boolean"
    },
    "closeOpenUiViews": {
      "type": "boolean"
    },
    "fast": {
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
