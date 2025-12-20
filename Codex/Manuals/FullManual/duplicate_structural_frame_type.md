# duplicate_structural_frame_type

- Category: ElementOps
- Purpose: Duplicate Structural Frame Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: duplicate_structural_frame_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| familyName | string | no/depends |  |
| newTypeName | string | no/depends |  |
| typeId | unknown | no/depends |  |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_structural_frame_type",
  "params": {
    "familyName": "...",
    "newTypeName": "...",
    "typeId": "...",
    "typeName": "..."
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
    "newTypeName": {
      "type": "string"
    },
    "typeName": {
      "type": "string"
    },
    "familyName": {
      "type": "string"
    },
    "typeId": {
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
