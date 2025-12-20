# toggle_dockable_pane

- Category: RevitUI
- Purpose: DockablePane の列挙 / 表示 / 非表示（UIスレッド実行 + モード選択）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: toggle_dockable_pane

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| mode | string | no/depends | both |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "toggle_dockable_pane",
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
