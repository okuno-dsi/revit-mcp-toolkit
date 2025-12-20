# activate_view

- Category: RevitUI
- Purpose: Manage model view windows (list/activate/open/tile/close-inactive)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: activate_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| name | unknown | no/depends |  |
| uniqueId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "activate_view",
  "params": {
    "name": "...",
    "uniqueId": "..."
  }
}
```

## Related
- list_open_views
- open_views
- close_inactive_views
- close_views
- close_views_except
- tile_windows
- list_dockable_panes
- show_dockable_pane

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "uniqueId": {
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
    "name": {
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
