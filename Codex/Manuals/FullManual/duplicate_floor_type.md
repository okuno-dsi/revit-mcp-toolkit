# duplicate_floor_type

- Category: ElementOps
- Purpose: Duplicate Floor Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: duplicate_floor_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| floorTypeId | int | no/depends |  |
| newName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_floor_type",
  "params": {
    "floorTypeId": 0,
    "newName": "..."
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
    "floorTypeId": {
      "type": "integer"
    },
    "newName": {
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
