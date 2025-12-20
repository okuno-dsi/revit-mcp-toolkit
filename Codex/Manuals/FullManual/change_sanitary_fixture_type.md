# change_sanitary_fixture_type

- Category: ElementOps
- Purpose: Change Sanitary Fixture Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: change_sanitary_fixture_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends | 0 |
| familyName | string | no/depends |  |
| newTypeId | int | no/depends | 0 |
| typeName | string | no/depends |  |
| uniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "change_sanitary_fixture_type",
  "params": {
    "elementId": 0,
    "familyName": "...",
    "newTypeId": 0,
    "typeName": "...",
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
    "newTypeId": {
      "type": "integer"
    },
    "uniqueId": {
      "type": "string"
    },
    "typeName": {
      "type": "string"
    },
    "familyName": {
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
