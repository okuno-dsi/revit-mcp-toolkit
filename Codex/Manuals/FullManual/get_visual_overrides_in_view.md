# get_visual_overrides_in_view

- Category: VisualizationOps
- Purpose: 指定ビューで「要素にグラフィックオーバーライドが適用されている」ものを列挙

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_visual_overrides_in_view
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_visual_overrides_in_view",
  "params": {}
}
```

## Related
- apply_conditional_coloring
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- prepare_sunstudy_view
- create_spatial_volume_overlay
- delete_spatial_volume_overlays

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
