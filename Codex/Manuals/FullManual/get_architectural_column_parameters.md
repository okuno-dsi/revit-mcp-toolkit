# get_architectural_column_parameters

- Category: ElementOps
- Purpose: Get Architectural Column Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_architectural_column_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends |  |
| includeDisplay | bool | no/depends | true |
| includeInstance | bool | no/depends | true |
| includeRaw | bool | no/depends | true |
| includeType | bool | no/depends | true |
| includeUnit | bool | no/depends | true |
| siDigits | int | no/depends | 3 |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_architectural_column_parameters",
  "params": {
    "count": 0,
    "elementId": 0,
    "includeDisplay": false,
    "includeInstance": false,
    "includeRaw": false,
    "includeType": false,
    "includeUnit": false,
    "siDigits": 0,
    "skip": 0
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
    "includeDisplay": {
      "type": "boolean"
    },
    "includeInstance": {
      "type": "boolean"
    },
    "siDigits": {
      "type": "integer"
    },
    "includeRaw": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "includeUnit": {
      "type": "boolean"
    },
    "includeType": {
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
