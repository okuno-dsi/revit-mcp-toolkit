# get_structural_column_type_parameters

- Category: ElementOps
- Purpose: Get Structural Column Type Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_structural_column_type_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_structural_column_type_parameters",
  "params": {
    "count": 0,
    "elementId": 0,
    "familyName": "...",
    "namesOnly": false,
    "skip": 0,
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "..."
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
    "elementId": {
      "type": "integer"
    },
    "skip": {
      "type": "integer"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "typeId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "count": {
      "type": "integer"
    },
    "typeName": {
      "type": "string"
    },
    "familyName": {
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
