# swap_roof_layer_materials

- Category: ElementOps
- Purpose: RoofType 用 追加コマンド

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: swap_roof_layer_materials

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| duplicateIfInUse | unknown | no/depends |  |
| find | unknown | no/depends |  |
| materialId | unknown | no/depends |  |
| materialName | unknown | no/depends |  |
| materialNameContains | unknown | no/depends |  |
| maxCount | unknown | no/depends |  |
| replace | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "swap_roof_layer_materials",
  "params": {
    "duplicateIfInUse": "...",
    "find": "...",
    "materialId": "...",
    "materialName": "...",
    "materialNameContains": "...",
    "maxCount": "...",
    "replace": "..."
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
    "materialName": {
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
    "materialId": {
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
    "maxCount": {
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
    "find": {
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
    "replace": {
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
    "materialNameContains": {
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
