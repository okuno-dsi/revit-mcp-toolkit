# list_structural_frame_parameters

- Category: ElementOps
- Purpose: List Structural Frame Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_structural_frame_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | unknown | no/depends |  |
| familyName | string | no/depends |  |
| typeId | unknown | no/depends |  |
| typeName | string | no/depends |  |
| uniqueId | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_structural_frame_parameters",
  "params": {
    "elementId": "...",
    "familyName": "...",
    "typeId": "...",
    "typeName": "...",
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
    "typeName": {
      "type": "string"
    },
    "uniqueId": {
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
    "familyName": {
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
