# get_ceiling_boundaries

- Category: ElementOps
- Purpose: Get Ceiling Boundaries in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_ceiling_boundaries

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| decimals | int | no/depends | 3 |
| elementId | int | no/depends | 0 |
| faceSelection | string | no/depends | bottom |
| flattenZ | bool | no/depends | true |
| includeEdges | bool | no/depends | false |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_ceiling_boundaries",
  "params": {
    "decimals": 0,
    "elementId": 0,
    "faceSelection": "...",
    "flattenZ": false,
    "includeEdges": false,
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
    "decimals": {
      "type": "integer"
    },
    "includeEdges": {
      "type": "boolean"
    },
    "uniqueId": {
      "type": "string"
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
