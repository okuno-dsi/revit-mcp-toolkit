# prepare_sunstudy_view

- Category: VisualizationOps
- Purpose: Prepare Sunstudy View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: prepare_sunstudy_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| baseName | string | no/depends | SunStudy 3D |
| detailLevel | string | no/depends | Fine |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "prepare_sunstudy_view",
  "params": {
    "baseName": "...",
    "detailLevel": "..."
  }
}
```

## Related
- apply_conditional_coloring
- clear_conditional_coloring
- refresh_view
- regen_and_refresh
- simulate_sunlight
- create_spatial_volume_overlay
- delete_spatial_volume_overlays
- set_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "baseName": {
      "type": "string"
    },
    "detailLevel": {
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
