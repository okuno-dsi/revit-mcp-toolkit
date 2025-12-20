# get_structural_columns

- Category: ElementOps
- Purpose: Get Structural Columns in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_structural_columns

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| pinned | bool | no/depends |  |
| skip | int | no/depends | 0 |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |
| withParameters | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_structural_columns",
  "params": {
    "count": 0,
    "elementId": 0,
    "familyName": "...",
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "pinned": false,
    "skip": 0,
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "...",
    "withParameters": false
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
    "levelId": {
      "type": "integer"
    },
    "nameContains": {
      "type": "string"
    },
    "typeName": {
      "type": "string"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "familyName": {
      "type": "string"
    },
    "count": {
      "type": "integer"
    },
    "elementId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "typeId": {
      "type": "integer"
    },
    "pinned": {
      "type": "boolean"
    },
    "levelName": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "withParameters": {
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
