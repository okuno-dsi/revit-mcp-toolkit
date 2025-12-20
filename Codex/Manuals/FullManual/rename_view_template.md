# rename_view_template

- Category: ViewOps
- Purpose: View Delete / View Template Assign-Clear / Save as Template / Rename Template

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: rename_view_template

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| newName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rename_view_template",
  "params": {
    "newName": "..."
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
    "newName": {
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
