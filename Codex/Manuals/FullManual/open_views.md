# open_views

- Category: RevitUI
- Purpose: Manage model view windows (list/activate/open/tile/close-inactive)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: open_views
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "open_views",
  "params": {}
}
```

## Related
- list_open_views
- activate_view
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
  "properties": {}
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
