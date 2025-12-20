# get_railings

- Category: ElementOps
- Purpose: Get Railings in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_railings

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| includeBaseline | bool | no/depends | true |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_railings",
  "params": {
    "count": 0,
    "includeBaseline": false,
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
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
    "count": {
      "type": "integer"
    },
    "levelId": {
      "type": "integer"
    },
    "skip": {
      "type": "integer"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "typeId": {
      "type": "integer"
    },
    "includeBaseline": {
      "type": "boolean"
    },
    "nameContains": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
    },
    "typeName": {
      "type": "string"
    },
    "namesOnly": {
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
