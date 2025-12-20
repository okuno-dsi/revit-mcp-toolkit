# apply_quick_color_scheme

- Category: VisualizationOps
- Purpose: Apply Quick Color Scheme in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: apply_quick_color_scheme

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaSchemeId | unknown | no/depends |  |
| autoWorkingView | bool | no/depends | true |
| bins | int | no/depends | 7 |
| categoryId | int | no/depends |  |
| createLegend | bool | no/depends | true |
| detachViewTemplate | bool | no/depends | false |
| maxClasses | int | no/depends | 12 |
| mode | string | no/depends | qualitative |
| palette | string | no/depends |  |
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "apply_quick_color_scheme",
  "params": {
    "areaSchemeId": "...",
    "autoWorkingView": false,
    "bins": 0,
    "categoryId": 0,
    "createLegend": false,
    "detachViewTemplate": false,
    "maxClasses": 0,
    "mode": "...",
    "palette": "...",
    "viewId": "..."
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
    "createLegend": {
      "type": "boolean"
    },
    "mode": {
      "type": "string"
    },
    "autoWorkingView": {
      "type": "boolean"
    },
    "maxClasses": {
      "type": "integer"
    },
    "bins": {
      "type": "integer"
    },
    "detachViewTemplate": {
      "type": "boolean"
    },
    "palette": {
      "type": "string"
    },
    "areaSchemeId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "viewId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "categoryId": {
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
