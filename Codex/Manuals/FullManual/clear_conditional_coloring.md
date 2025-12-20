# clear_conditional_coloring

- Category: VisualizationOps
- Purpose: Clear Conditional Coloring in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: clear_conditional_coloring

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| alsoResetAllViewOverrides | bool | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clear_conditional_coloring",
  "params": {
    "alsoResetAllViewOverrides": false,
    "viewId": 0
  }
}
```

## Related
- apply_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "viewId": {
      "type": "integer"
    },
    "alsoResetAllViewOverrides": {
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
