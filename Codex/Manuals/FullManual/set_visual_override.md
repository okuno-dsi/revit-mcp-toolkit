# set_visual_override

- Category: VisualizationOps
- Purpose: Set Visual Override in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.
The implementation applies a solid fill (surface + cut) and line color overrides, but the visual result still depends on the active view:
- If the view is in Wireframe or surface patterns are turned off in Visibility/Graphics or the view template, the override may appear only as line color.
- Switching the view style to Hidden Line / Shaded and enabling surface patterns will make the solid fill visible.
- If the target view has a View Template applied and you do not set `detachViewTemplate:true`, the command does **not** modify any graphics and instead returns
  `templateApplied: true`, `templateViewId`, `skippedDueToTemplate: true`, `errorCode: "VIEW_TEMPLATE_LOCK"`, and a message instructing you to detach the template.

## Usage
- Method: set_visual_override

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoWorkingView | bool | no/depends | true |
| b | int | no/depends | 0 |
| batchSize | int | no/depends | 800 |
| detachViewTemplate | bool | no/depends | false |
| g | int | no/depends | 0 |
| maxMillisPerTx | int | no/depends | 3000 |
| r | int | no/depends | 255 |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| transparency | int | no/depends | 40 |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_visual_override",
  "params": {
    "autoWorkingView": false,
    "b": 0,
    "batchSize": 0,
    "detachViewTemplate": false,
    "g": 0,
    "maxMillisPerTx": 0,
    "r": 0,
    "refreshView": false,
    "startIndex": 0,
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

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "startIndex": {
      "type": "integer"
    },
    "autoWorkingView": {
      "type": "boolean"
    },
    "refreshView": {
      "type": "boolean"
    },
    "g": {
      "type": "integer"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "transparency": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
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
