# create_spatial_volume_overlay

- Category: VisualizationOps
- Purpose: Create Spatial Volume Overlay in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_spatial_volume_overlay

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 200 |
| maxMillisPerTx | int | no/depends | 3000 |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| target | string | no/depends | room |
| transparency | int | no/depends | 60 |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_spatial_volume_overlay",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "target": "...",
    "transparency": 0,
    "viewId": 0
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
- delete_spatial_volume_overlays
- set_visual_override

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "startIndex": {
      "type": "integer"
    },
    "transparency": {
      "type": "integer"
    },
    "viewId": {
      "type": "integer"
    },
    "refreshView": {
      "type": "boolean"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "target": {
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
