# regen_and_refresh

- Category: VisualizationOps
- Purpose: Force UI redraw and/or document regenerate.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: regen_and_refresh
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "regen_and_refresh",
  "params": {}
}
```

## Related
- apply_conditional_coloring
- clear_conditional_coloring
- refresh_view
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override

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
