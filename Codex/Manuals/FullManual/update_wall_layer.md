# update_wall_layer

- Category: ElementOps
- Purpose: Update Wall Layer in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_wall_layer

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| layerIndex | int | no/depends |  |
| materialName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_wall_layer",
  "params": {
    "layerIndex": 0,
    "materialName": "..."
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
    "layerIndex": {
      "type": "integer"
    },
    "materialName": {
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
