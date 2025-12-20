# update_material_parameter

- Category: ElementOps
- Purpose: Update Material Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_material_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | no/depends | 0 |
| paramName | string | no/depends |  |
| uniqueId | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_material_parameter",
  "params": {
    "materialId": 0,
    "paramName": "...",
    "uniqueId": "...",
    "value": "..."
  }
}
```

## Related
- get_materials
- get_material_parameters
- list_material_parameters
- duplicate_material
- rename_material
- delete_material
- create_material
- apply_material_to_element

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "paramName": {
      "type": "string"
    },
    "value": {
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
    "uniqueId": {
      "type": "string"
    },
    "materialId": {
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
