# clear_visual_override

- Category: VisualizationOps
- Purpose: Clear Visual Override in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: clear_visual_override

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoWorkingView | bool | no/depends | true |
| batchSize | int | no/depends | 800 |
| detachViewTemplate | bool | no/depends | false |
| maxMillisPerTx | int | no/depends | 3000 |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "clear_visual_override",
  "params": {
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
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
- If the target view has a View Template applied and you do not set `detachViewTemplate:true`, no overrides are cleared.  
  The result includes `templateApplied: true`, `templateViewId`, `skippedDueToTemplate: true`, `errorCode: "VIEW_TEMPLATE_LOCK"`, and a message
  telling you to detach the template before calling `clear_visual_override`.

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
    "detachViewTemplate": {
      "type": "boolean"
    },
    "batchSize": {
      "type": "integer"
    },
    "maxMillisPerTx": {
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
