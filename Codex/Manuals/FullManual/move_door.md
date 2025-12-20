# move_door

- Category: ElementOps
- Purpose: Move Door in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: move_door

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dx | number | no/depends | 0.0 |
| dy | number | no/depends | 0.0 |
| dz | number | no/depends | 0.0 |
| elementId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_door",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0
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
    "dz": {
      "type": "number"
    },
    "elementId": {
      "type": "integer"
    },
    "dy": {
      "type": "number"
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
