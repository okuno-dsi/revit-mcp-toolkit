# set_wall_variable_layer

- Category: ElementOps
- Purpose: Set Wall Variable Layer in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_wall_variable_layer

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| isVariable | bool | no/depends |  |
| layerIndex | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_variable_layer",
  "params": {
    "isVariable": false,
    "layerIndex": 0
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
    "isVariable": {
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
