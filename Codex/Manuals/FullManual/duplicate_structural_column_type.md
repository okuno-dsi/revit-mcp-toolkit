# duplicate_structural_column_type

- Category: ElementOps
- Purpose: Duplicate Structural Column Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: duplicate_structural_column_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| newTypeName | unknown | no/depends |  |
| typeId | unknown | no/depends |  |
| typeName | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_structural_column_type",
  "params": {
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
    "typeName": {
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
