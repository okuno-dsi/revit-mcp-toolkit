# get_instance_geometry

- Category: ElementOps
- Purpose: Get Instance Geometry in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_instance_geometry

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detailLevel | string | no/depends |  |
| elementId | unknown | no/depends |  |
| includeNonVisible | bool | no/depends | false |
| uniqueId | unknown | no/depends |  |
| weld | bool | no/depends | true |
| weldTolerance | number | no/depends | 1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_instance_geometry",
  "params": {
    "detailLevel": "...",
    "elementId": "...",
    "includeNonVisible": false,
    "uniqueId": "...",
    "weld": false,
    "weldTolerance": 0.0
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
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
    },
    "weld": {
      "type": "boolean"
    },
    "detailLevel": {
      "type": "string"
    },
    "includeNonVisible": {
      "type": "boolean"
    },
    "weldTolerance": {
      "type": "number"
    },
    "uniqueId": {
      "type": [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
      ]
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
