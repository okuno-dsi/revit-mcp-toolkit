# add_wall_layer

- Category: ElementOps
- Purpose: Add Wall Layer in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: add_wall_layer

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| function | string | no/depends | Structure |
| insertAt | int | no/depends | 0 |
| isCore | bool | no/depends | false |
| isVariable | bool | no/depends | false |
| materialId | int | no/depends | 0 |
| materialName | string | no/depends |  |
| thicknessMm | number | no/depends | 1.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_wall_layer",
  "params": {
    "function": "...",
    "insertAt": 0,
    "isCore": false,
    "isVariable": false,
    "materialId": 0,
    "materialName": "...",
    "thicknessMm": 0.0
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
    "isVariable": {
      "type": "boolean"
    },
    "thicknessMm": {
      "type": "number"
    },
    "materialName": {
      "type": "string"
    },
    "isCore": {
      "type": "boolean"
    },
    "materialId": {
      "type": "integer"
    },
    "function": {
      "type": "string"
    },
    "insertAt": {
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
