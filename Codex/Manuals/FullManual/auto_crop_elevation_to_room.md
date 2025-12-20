# auto_crop_elevation_to_room

- Category: ViewOps
- Purpose: Interior Elevation（展開図）を部屋境界に合わせて自動クロップ

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: auto_crop_elevation_to_room

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| boundaryLocation | string | no/depends | Finish |
| clipDepthMm | number | no/depends | 1000.0 |
| minHeightMm | number | no/depends | 1200.0 |
| minWidthMm | number | no/depends | 1200.0 |
| paddingMm | number | no/depends | 200.0 |
| roomId | int | no/depends |  |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "auto_crop_elevation_to_room",
  "params": {
    "boundaryLocation": "...",
    "clipDepthMm": 0.0,
    "minHeightMm": 0.0,
    "minWidthMm": 0.0,
    "paddingMm": 0.0,
    "roomId": 0,
    "viewId": 0
  }
}
```

## Related
- get_current_view
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "roomId": {
      "type": "integer"
    },
    "boundaryLocation": {
      "type": "string"
    },
    "minHeightMm": {
      "type": "number"
    },
    "clipDepthMm": {
      "type": "number"
    },
    "paddingMm": {
      "type": "number"
    },
    "viewId": {
      "type": "integer"
    },
    "minWidthMm": {
      "type": "number"
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
