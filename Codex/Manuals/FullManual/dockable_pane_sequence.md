# dockable_pane_sequence

- Category: RevitUI
- Purpose: Dockable Pane Sequence in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: dockable_pane_sequence

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| continueOnError | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "dockable_pane_sequence",
  "params": {
    "continueOnError": false
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
    "continueOnError": {
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
