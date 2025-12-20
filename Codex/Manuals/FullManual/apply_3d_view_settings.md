# apply_3d_view_settings

- Category: ViewOps
- Purpose: 3Dビューの状態（姿勢・Crop/Section・Transform・FOV）を保存/適用

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: apply_3d_view_settings

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| allowPerspectiveCrop | bool | no/depends | false |
| createNewViewOnTypeMismatch | bool | no/depends | true |
| cropActive | bool | no/depends |  |
| cropVisible | bool | no/depends |  |
| fitOnApply | bool | no/depends | true |
| isPerspective | bool | no/depends |  |
| sectionActive | bool | no/depends |  |
| snapCropToView | bool | no/depends | true |
| switchActive | bool | no/depends | false |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_3d_view_settings",
  "params": {
    "allowPerspectiveCrop": false,
    "createNewViewOnTypeMismatch": false,
    "cropActive": false,
    "cropVisible": false,
    "fitOnApply": false,
    "isPerspective": false,
    "sectionActive": false,
    "snapCropToView": false,
    "switchActive": false,
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
    "fitOnApply": {
      "type": "boolean"
    },
    "switchActive": {
      "type": "boolean"
    },
    "sectionActive": {
      "type": "boolean"
    },
    "createNewViewOnTypeMismatch": {
      "type": "boolean"
    },
    "snapCropToView": {
      "type": "boolean"
    },
    "allowPerspectiveCrop": {
      "type": "boolean"
    },
    "cropVisible": {
      "type": "boolean"
    },
    "isPerspective": {
      "type": "boolean"
    },
    "viewId": {
      "type": "integer"
    },
    "cropActive": {
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
