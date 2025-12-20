# get_floor_boundary

- Category: ElementOps
- Purpose: Get Floor Boundary in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_floor_boundary

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| decimals | int | no/depends | 3 |
| elementId | int | no/depends | 0 |
| faceSelection | string | no/depends | top |
| flattenZ | bool | no/depends | true |
| includeCurveType | bool | no/depends | true |
| includeLength | bool | no/depends | true |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_floor_boundary",
  "params": {
    "decimals": 0,
    "elementId": 0,
    "faceSelection": "...",
    "flattenZ": false,
    "includeCurveType": false,
    "includeLength": false,
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
    "flattenZ": {
      "type": "boolean"
    },
    "includeCurveType": {
      "type": "boolean"
    },
    "decimals": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "includeLength": {
      "type": "boolean"
    },
    "faceSelection": {
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
