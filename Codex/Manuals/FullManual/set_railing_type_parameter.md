# set_railing_type_parameter

- Category: ElementOps
- Purpose: Set Railing Type Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_railing_type_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| paramName | string | no/depends |  |
| typeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| value | unknown | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_railing_type_parameter",
  "params": {
    "paramName": "...",
    "typeId": 0,
    "typeName": "...",
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
    "typeId": {
      "type": "integer"
    },
    "paramName": {
      "type": "string"
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
    "typeName": {
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
