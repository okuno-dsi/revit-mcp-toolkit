# duplicate_material

- Category: ElementOps
- Purpose: Duplicate Material in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: duplicate_material

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | no/depends | 0 |
| newName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_material",
  "params": {
    "materialId": 0,
    "newName": "...",
    "uniqueId": "..."
  }
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- update_material_parameter
- rename_material
- delete_material
- create_material
- apply_material_to_element

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "materialId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
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
