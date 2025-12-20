# get_walls

- Category: ElementOps
- Purpose: Get Walls in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_walls

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| skip | int | no/depends | 0 |
| viewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_walls",
  "params": {
    "count": 0,
    "skip": 0,
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
