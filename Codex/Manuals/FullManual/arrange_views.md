# arrange_views

- Category: RevitUI
- Purpose: Manage model view windows (list/activate/open/tile/close-inactive)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: arrange_views

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| mode | string | no/depends | tile |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "arrange_views",
  "params": {
    "mode": "..."
  }
}
```

## Related
- list_open_views
- activate_view
- open_views
- close_inactive_views
- close_views
- close_views_except
- tile_windows
- list_dockable_panes

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "mode": {
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
