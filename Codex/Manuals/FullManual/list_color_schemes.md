# list_color_schemes

- Category: VisualizationOps
- Purpose: List Color Schemes in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_color_schemes

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaSchemeId | unknown | no/depends |  |
| categoryId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_color_schemes",
  "params": {
    "areaSchemeId": "...",
    "categoryId": "..."
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
    "categoryId": {
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
