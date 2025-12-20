# save_3d_view_settings

- Category: ViewOps
- Purpose: 3Dビューの状態（姿勢・Crop/Section・Transform・FOV）を保存/適用

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: save_3d_view_settings

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| deriveCropFromViewExtents | bool | no/depends | true |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "save_3d_view_settings",
  "params": {
    "deriveCropFromViewExtents": false,
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
    "viewId": {
      "type": "integer"
    },
    "deriveCropFromViewExtents": {
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
