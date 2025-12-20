# replace_stacked_wall_part_type

- Category: ElementOps
- Purpose: Replace Stacked Wall Part Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: replace_stacked_wall_part_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| childTypeId | unknown | no/depends |  |
| childTypeName | unknown | no/depends |  |
| partIndex | int | no/depends | -1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "replace_stacked_wall_part_type",
  "params": {
    "childTypeId": "...",
    "childTypeName": "...",
    "partIndex": 0
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
    "childTypeName": {
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
    "childTypeId": {
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
    "partIndex": {
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
