# get_curves_by_category

- Category: DxfOps
- Purpose: Get Curves By Category in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_curves_by_category

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| arcsOnly | bool | no/depends | false |
| limit | int | no/depends |  |
| linesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_curves_by_category",
  "params": {
    "arcsOnly": false,
    "limit": 0,
    "linesOnly": false,
    "skip": 0
  }
}
```

## Related
- get_grids_with_bubbles
- export_curves_to_dxf
- generate_dwg_merge_script_manual

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "skip": {
      "type": "integer"
    },
    "linesOnly": {
      "type": "boolean"
    },
    "arcsOnly": {
      "type": "boolean"
    },
    "limit": {
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
