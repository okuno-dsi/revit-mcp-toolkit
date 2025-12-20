# get_family_type_parameters

- Category: ElementOps
- Purpose: Get Family Type Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_family_type_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryId | int | no/depends |  |
| categoryName | string | no/depends |  |
| count | int | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_family_type_parameters",
  "params": {
    "categoryId": 0,
    "categoryName": "...",
    "count": 0,
    "namesOnly": false,
    "skip": 0,
    "typeId": 0,
    "typeName": "..."
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
    "categoryName": {
      "type": "string"
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
    "categoryId": {
      "type": "integer"
    },
    "count": {
      "type": "integer"
    },
    "typeName": {
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
