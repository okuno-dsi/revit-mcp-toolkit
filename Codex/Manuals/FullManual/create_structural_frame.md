# create_structural_frame

- Category: ElementOps
- Purpose: Create Structural Frame in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_structural_frame

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| end | object | no/depends |  |
| familyName | string | no/depends |  |
| levelId | unknown | no/depends |  |
| levelName | unknown | no/depends |  |
| start | object | no/depends |  |
| typeId | unknown | no/depends |  |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_structural_frame",
  "params": {
    "end": "...",
    "familyName": "...",
    "levelId": "...",
    "levelName": "...",
    "start": "...",
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
    "start": {
      "type": "object"
    },
    "levelId": {
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
    "end": {
      "type": "object"
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
    "levelName": {
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
