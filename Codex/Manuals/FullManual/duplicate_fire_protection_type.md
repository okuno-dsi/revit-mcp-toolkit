# duplicate_fire_protection_type

- Category: FireProtection
- Purpose: Duplicate Fire Protection Type in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: duplicate_fire_protection_type

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| newTypeName | string | no/depends |  |
| sourceTypeId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "duplicate_fire_protection_type",
  "params": {
    "newTypeName": "...",
    "sourceTypeId": 0
  }
}
```

## Related
- get_fire_protection_instances
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- get_fire_protection_parameters
- set_fire_protection_parameter
- get_fire_protection_types
- delete_fire_protection_type

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "newTypeName": {
      "type": "string"
    },
    "sourceTypeId": {
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
