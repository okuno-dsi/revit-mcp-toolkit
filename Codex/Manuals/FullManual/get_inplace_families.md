# get_inplace_families

- Category: ElementOps
- Purpose: Get Inplace Families in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_inplace_families

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| category | string | no/depends |  |
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_inplace_families",
  "params": {
    "category": "...",
    "count": 0,
    "elementId": 0,
    "familyName": "...",
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
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
    "levelId": {
      "type": "integer"
    },
    "skip": {
      "type": "integer"
    },
    "typeName": {
      "type": "string"
    },
    "category": {
      "type": "string"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "uniqueId": {
      "type": "string"
    },
    "nameContains": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
    },
    "count": {
      "type": "integer"
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
