# get_curtain_wall_schedule

- Category: ElementOps
- Purpose: Get Curtain Wall Schedule in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_curtain_wall_schedule

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| areaMethod | string | no/depends | param |
| filterPanelMaterialContains | string | no/depends |  |
| includeBreakdown | bool | no/depends | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_curtain_wall_schedule",
  "params": {
    "areaMethod": "...",
    "filterPanelMaterialContains": "...",
    "includeBreakdown": false
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
    "areaMethod": {
      "type": "string"
    },
    "includeBreakdown": {
      "type": "boolean"
    },
    "filterPanelMaterialContains": {
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
