# create_sheet

- Category: ViewOps
- Purpose: Create Sheet in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_sheet

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| noTitleBlock | bool | no/depends | false |
| sheetName | string | no/depends |  |
| sheetNumber | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_sheet",
  "params": {
    "noTitleBlock": false,
    "sheetName": "...",
    "sheetNumber": "..."
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
    "sheetNumber": {
      "type": "string"
    },
    "noTitleBlock": {
      "type": "boolean"
    },
    "sheetName": {
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
