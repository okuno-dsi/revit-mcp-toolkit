# close_views_except

- Category: RevitUI
- Purpose: Manage model view windows (list/activate/open/tile/close-inactive)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: close_views_except
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "close_views_except",
  "params": {}
}
```

## Related
- list_open_views
- activate_view
- open_views
- close_inactive_views
- close_views
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
