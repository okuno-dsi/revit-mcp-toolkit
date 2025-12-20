# flip_family_instance_orientation

- Category: ElementOps
- Purpose: FamilyInstance 全般の Hand/Facing 反転・ミラー・回転（dryRun対応）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: flip_family_instance_orientation

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dryRun | bool | no/depends | false |
| rotateDeg | number | no/depends | 0.0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "flip_family_instance_orientation",
  "params": {
    "dryRun": false,
    "rotateDeg": 0.0
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
    "dryRun": {
      "type": "boolean"
    },
    "rotateDeg": {
      "type": "number"
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
