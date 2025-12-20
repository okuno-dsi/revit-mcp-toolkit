# add_curtain_wall_panel

- Category: ElementOps
- Purpose: Add Curtain Wall Panel in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: add_curtain_wall_panel

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| panelIndex | int | no/depends | -1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_curtain_wall_panel",
  "params": {
    "panelIndex": 0
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
    "panelIndex": {
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
