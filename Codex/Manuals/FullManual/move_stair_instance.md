# move_stair_instance

- Category: ElementOps
- Purpose: Move Stair Instance in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_stair_instance

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends | 0 |
| dy | number | no/depends | 0 |
| dz | number | no/depends | 0 |
| elementId | int | no/depends | 0 |
| offset | object | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_stair_instance",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0,
    "offset": "...",
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
    "dz": {
      "type": "number"
    },
    "uniqueId": {
      "type": "string"
    },
    "dy": {
      "type": "number"
    },
    "offset": {
      "type": "object"
    },
    "dx": {
      "type": "number"
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
