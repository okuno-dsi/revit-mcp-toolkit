# list_view_colorfill_categories

- Category: VisualizationOps
- Purpose: List View Colorfill Categories in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_view_colorfill_categories

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_view_colorfill_categories",
  "params": {
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
