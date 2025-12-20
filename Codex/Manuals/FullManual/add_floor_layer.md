# add_floor_layer

- Category: ElementOps
- Purpose: FloorType の CompoundStructure レイヤ取得/編集

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: add_floor_layer

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| duplicateIfInUse | unknown | no/depends |  |
| function | unknown | no/depends |  |
| insertAt | unknown | no/depends |  |
| isCore | unknown | no/depends |  |
| isVariable | unknown | no/depends |  |
| thicknessMm | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_floor_layer",
  "params": {
    "duplicateIfInUse": "...",
    "function": "...",
    "insertAt": "...",
    "isCore": "...",
    "isVariable": "...",
    "thicknessMm": "..."
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
    "isVariable": {
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
    "thicknessMm": {
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
    "isCore": {
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
    "duplicateIfInUse": {
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
    "function": {
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
    "insertAt": {
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
