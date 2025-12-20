# update_structural_frame_geometry

- Category: ElementOps
- Purpose: Update Structural Frame Geometry in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_structural_frame_geometry

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends | 0 |
| end | object | no/depends |  |
| start | object | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_structural_frame_geometry",
  "params": {
    "elementId": 0,
    "end": "...",
    "start": "...",
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
      "type": "integer"
    },
    "end": {
      "type": "object"
    },
    "uniqueId": {
      "type": "string"
    },
    "start": {
      "type": "object"
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
