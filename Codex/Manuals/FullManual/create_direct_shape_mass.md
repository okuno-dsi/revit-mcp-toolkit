# create_direct_shape_mass

- Category: ElementOps
- Purpose: Create Direct Shape Mass in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_direct_shape_mass

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| height | number | no/depends |  |
| loops | array | no/depends |  |
| useMassCategory | bool | no/depends | true |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_direct_shape_mass",
  "params": {
    "height": 0.0,
    "loops": "...",
    "useMassCategory": false
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
    "height": {
      "type": "number"
    },
    "useMassCategory": {
      "type": "boolean"
    },
    "loops": {
      "type": "array"
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
