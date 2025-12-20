# get_windows

- Category: ElementOps
- Purpose: Get Windows in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_windows

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| hostUniqueId | string | no/depends |  |
| levelId | int | no/depends | 0 |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |
| wallId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_windows",
  "params": {
    "count": 0,
    "elementId": 0,
    "familyName": "...",
    "hostUniqueId": "...",
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
    "typeId": 0,
    "typeName": "...",
    "uniqueId": "...",
    "wallId": 0
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
    "typeId": {
      "type": "integer"
    },
    "familyName": {
      "type": "string"
    },
    "wallId": {
      "type": "integer"
    },
    "levelName": {
      "type": "string"
    },
    "skip": {
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
