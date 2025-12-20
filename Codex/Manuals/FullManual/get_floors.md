# get_floors

- Category: ElementOps
- Purpose: Get Floors in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_floors

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| includeLocation | bool | no/depends | true |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| summaryOnly | bool | no/depends | false |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_floors",
  "params": {
    "count": 0,
    "includeLocation": false,
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
    "viewId": 0
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
    "skip": {
      "type": "integer"
    },
    "summaryOnly": {
      "type": "boolean"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "includeLocation": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "viewId": {
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
