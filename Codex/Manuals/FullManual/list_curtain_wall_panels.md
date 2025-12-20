# list_curtain_wall_panels

- Category: ElementOps
- Purpose: List Curtain Wall Panels in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_curtain_wall_panels

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| namesOnly | bool | no/depends | false |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_curtain_wall_panels",
  "params": {
    "count": 0,
    "namesOnly": false,
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
    "skip": {
      "type": "integer"
    },
    "namesOnly": {
      "type": "boolean"
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
