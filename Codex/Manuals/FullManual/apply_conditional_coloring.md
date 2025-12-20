# apply_conditional_coloring

- Category: VisualizationOps
- Purpose: Apply Conditional Coloring in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: apply_conditional_coloring

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoWorkingView | bool | no/depends | true |
| batchSize | int | no/depends | 800 |
| detachViewTemplate | bool | no/depends | false |
| maxMillisPerTx | int | no/depends | 3000 |
| mode | string | no/depends | by_element_ranges |
| refreshView | bool | no/depends | true |
| startIndex | int | no/depends | 0 |
| units | string | no/depends | auto |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_conditional_coloring",
  "params": {
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "mode": "...",
    "refreshView": false,
    "startIndex": 0,
    "units": "...",
    "viewId": 0
  }
}
```

## Related
- clear_conditional_coloring
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
    "startIndex": {
      "type": "integer"
    },
    "mode": {
      "type": "string"
    },
    "refreshView": {
      "type": "boolean"
    },
    "batchSize": {
      "type": "integer"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "autoWorkingView": {
      "type": "boolean"
    },
    "units": {
      "type": "string"
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
