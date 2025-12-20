# change_railing_type

- Category: ElementOps
- Purpose: Change Railing Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: change_railing_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "change_railing_type",
  "params": {
    "typeId": 0,
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
    "typeName": {
      "type": "string"
    },
    "typeId": {
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
