# get_doors

- Category: ElementOps
- Purpose: Get Doors in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_doors

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| hostUniqueId | string | no/depends |  |
| hostWallId | int | no/depends | 0 |
| includeLocation | bool | no/depends | true |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_doors",
  "params": {
    "count": 0,
    "elementId": 0,
    "familyName": "...",
    "hostUniqueId": "...",
    "hostWallId": 0,
    "includeLocation": false,
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
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
    "levelId": {
      "type": "integer"
    },
    "nameContains": {
      "type": "string"
    },
    "typeName": {
      "type": "string"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "hostUniqueId": {
      "type": "string"
    },
    "namesOnly": {
      "type": "boolean"
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
    "hostWallId": {
      "type": "integer"
    },
    "typeId": {
      "type": "integer"
    },
    "familyName": {
      "type": "string"
    },
    "levelName": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "includeLocation": {
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
