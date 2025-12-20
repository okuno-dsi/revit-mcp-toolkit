# get_sanitary_fixtures

- Category: ElementOps
- Purpose: Get Sanitary Fixtures in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_sanitary_fixtures

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| familyName | string | no/depends |  |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_sanitary_fixtures",
  "params": {
    "count": 0,
    "familyName": "...",
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
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
    "count": {
      "type": "integer"
    },
    "levelId": {
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
    "nameContains": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
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
