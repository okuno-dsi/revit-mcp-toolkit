# debug_resolve_dockable_pane

- Category: RevitUI
- Purpose: Debug Resolve Dockable Pane in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: debug_resolve_dockable_pane
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "debug_resolve_dockable_pane",
  "params": {}
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
