# create_ceiling

- Category: ElementOps
- Purpose: Create Ceiling in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_ceiling

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | int | no/depends |  |
| offsetMm | number | no/depends | 0.0 |
| typeId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_ceiling",
  "params": {
    "levelId": 0,
    "offsetMm": 0.0,
    "typeId": 0
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
    "offsetMm": {
      "type": "number"
    },
    "typeId": {
      "type": "integer"
    },
    "levelId": {
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
