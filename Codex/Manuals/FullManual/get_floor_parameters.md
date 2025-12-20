# get_floor_parameters

- Category: ElementOps
- Purpose: Get Floor Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_floor_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| elementId | int | no/depends | 0 |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_floor_parameters",
  "params": {
    "count": 0,
    "elementId": 0,
    "namesOnly": false,
    "skip": 0,
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
    "skip": {
      "type": "integer"
    },
    "elementId": {
      "type": "integer"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "uniqueId": {
      "type": "string"
    },
    "count": {
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
