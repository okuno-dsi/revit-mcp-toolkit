# get_instances_geometry

- Category: ElementOps
- Purpose: Get Instances Geometry in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_instances_geometry

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detailLevel | string | no/depends |  |
| elementIds | unknown | no/depends |  |
| fromSelection | bool | no/depends | false |
| includeAnalytic | bool | no/depends | false |
| includeNonVisible | bool | no/depends | false |
| uniqueIds | unknown | no/depends |  |
| weld | bool | no/depends | true |
| weldTolerance | number | no/depends | 1 |
| weldToleranceMm | number | no/depends | -1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_instances_geometry",
  "params": {
    "detailLevel": "...",
    "elementIds": "...",
    "fromSelection": false,
    "includeAnalytic": false,
    "includeNonVisible": false,
    "uniqueIds": "...",
    "weld": false,
    "weldTolerance": 0.0,
    "weldToleranceMm": 0.0
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
    "includeAnalytic": {
      "type": "boolean"
    },
    "weld": {
      "type": "boolean"
    },
    "weldTolerance": {
      "type": "number"
    },
    "detailLevel": {
      "type": "string"
    },
    "fromSelection": {
      "type": "boolean"
    },
    "weldToleranceMm": {
      "type": "number"
    },
    "includeNonVisible": {
      "type": "boolean"
    },
    "uniqueIds": {
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
    "elementIds": {
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
