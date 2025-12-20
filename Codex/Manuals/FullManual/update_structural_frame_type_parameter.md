# update_structural_frame_type_parameter

- Category: ElementOps
- Purpose: Update Structural Frame Type Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_structural_frame_type_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| familyName | string | no/depends |  |
| paramName | string | no/depends |  |
| typeId | unknown | no/depends |  |
| typeName | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_structural_frame_type_parameter",
  "params": {
    "familyName": "...",
    "paramName": "...",
    "typeId": "...",
    "typeName": "...",
    "value": "..."
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
    "paramName": {
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
