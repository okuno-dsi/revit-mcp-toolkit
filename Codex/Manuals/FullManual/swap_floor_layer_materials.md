# swap_floor_layer_materials

- Category: ElementOps
- Purpose: FloorType の CompoundStructure レイヤ取得/編集

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: swap_floor_layer_materials

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| maxCount | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "swap_floor_layer_materials",
  "params": {
    "maxCount": 0
  }
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "maxCount": {
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
