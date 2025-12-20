# export_curves_to_dxf

- Category: DxfOps
- Purpose: Export Curves To Dxf in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_curves_to_dxf

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| inputUnit | string | no/depends | mm |
| layerName | string | no/depends | 0 |
| outputFilePath | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_curves_to_dxf",
  "params": {
    "inputUnit": "...",
    "layerName": "...",
    "outputFilePath": "..."
  }
}
```

## Related
- get_curves_by_category
- get_grids_with_bubbles
- generate_dwg_merge_script_manual

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "outputFilePath": {
      "type": "string"
    },
    "layerName": {
      "type": "string"
    },
    "inputUnit": {
      "type": "string"
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
