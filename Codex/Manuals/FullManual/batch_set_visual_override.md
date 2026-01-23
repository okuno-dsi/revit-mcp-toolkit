# batch_set_visual_override

- Category: VisualizationOps
- Purpose: Batch Set Visual Override in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: batch_set_visual_override

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| b | int | no/depends | 80 |
| detachViewTemplate | bool | no/depends | true |
| g | int | no/depends | 230 |
| lineRgb | object | no/depends |  | Optional line color override `{r,g,b}` (0-255). If omitted, uses `r/g/b`. |
| r | int | no/depends | 200 |
| fillRgb | object | no/depends |  | Optional fill (surface/cut patterns) color `{r,g,b}` (0-255). If omitted, uses `r/g/b`. |
| transparency | int | no/depends | 40 |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "batch_set_visual_override",
  "params": {
    "b": 0,
    "detachViewTemplate": false,
    "g": 0,
    "r": 0,
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
- create_spatial_volume_overlay
- delete_spatial_volume_overlays

### View Template behaviour
- If the target view has a View Template applied and you do not set `detachViewTemplate:true`, no overrides are written.  
  The result includes `templateApplied: true`, `templateViewId`, `skippedDueToTemplate: true`, `errorCode: "VIEW_TEMPLATE_LOCK"`, and a message
  telling you to detach the template before calling `batch_set_visual_override`.

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "transparency": {
      "type": "integer"
    },
    "g": {
      "type": "integer"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "r": {
      "type": "integer"
    },
    "b": {
      "type": "integer"
    },
    "viewId": {
      "type": "integer"
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
