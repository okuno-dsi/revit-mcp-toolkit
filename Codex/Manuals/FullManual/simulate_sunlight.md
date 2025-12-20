# simulate_sunlight

- Category: VisualizationOps
- Purpose: Simulate Sunlight in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: simulate_sunlight
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "simulate_sunlight",
  "params": {}
}
```

## Related
- apply_conditional_coloring
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
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
