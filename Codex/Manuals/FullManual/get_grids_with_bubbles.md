# get_grids_with_bubbles

- Category: DxfOps
- Purpose: Get Grids With Bubbles in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_grids_with_bubbles

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| layer | string | no/depends | 0 |
| radiusMm | number | no/depends | 100.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_grids_with_bubbles",
  "params": {
    "layer": "...",
    "radiusMm": 0.0
  }
}
```

## Related
- get_curves_by_category
- export_curves_to_dxf
- generate_dwg_merge_script_manual

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "layer": {
      "type": "string"
    },
    "radiusMm": {
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
