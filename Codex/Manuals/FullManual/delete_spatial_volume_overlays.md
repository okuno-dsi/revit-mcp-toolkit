# delete_spatial_volume_overlays

- Category: VisualizationOps
- Purpose: Delete Spatial Volume Overlays in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_spatial_volume_overlays

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| all | bool | no/depends | false |
| dryRun | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_spatial_volume_overlays",
  "params": {
    "all": false,
    "dryRun": false
  }
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
- set_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "all": {
      "type": "boolean"
    },
    "dryRun": {
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
