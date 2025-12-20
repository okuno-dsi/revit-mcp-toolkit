# generate_dwg_merge_script_manual

- Category: DxfOps
- Purpose: Generate Dwg Merge Script Manual in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: generate_dwg_merge_script_manual

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| fileName | string | no/depends |  |
| outPath | string | no/depends |  |
| returnContent | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "generate_dwg_merge_script_manual",
  "params": {
    "fileName": "...",
    "outPath": "...",
    "returnContent": false
  }
}
```

## Related
- get_curves_by_category
- get_grids_with_bubbles
- export_curves_to_dxf

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "outPath": {
      "type": "string"
    },
    "fileName": {
      "type": "string"
    },
    "returnContent": {
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
