# delete_mullion

- Category: ElementOps
- Purpose: Delete Mullion in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: delete_mullion

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| mullionIndex | int | no/depends | -1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_mullion",
  "params": {
    "mullionIndex": 0
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
    "mullionIndex": {
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
