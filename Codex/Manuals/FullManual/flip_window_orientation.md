# flip_window_orientation

- Category: ElementOps
- Purpose: 窓(Window: OST_Windows) の Hand/Facing 反転・任意ミラー・任意回転

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: flip_window_orientation

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dryRun | bool | no/depends | false |
| elementId | unknown | no/depends |  |
| rotateDeg | number | no/depends | 0.0 |
| uniqueId | unknown | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "flip_window_orientation",
  "params": {
    "dryRun": false,
    "elementId": "...",
    "rotateDeg": 0.0,
    "uniqueId": "...",
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
    "rotateDeg": {
      "type": "number"
    },
    "dryRun": {
      "type": "boolean"
    },
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
