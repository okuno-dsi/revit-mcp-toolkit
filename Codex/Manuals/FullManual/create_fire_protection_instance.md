# create_fire_protection_instance

- Category: FireProtection
- Purpose: Create Fire Protection Instance in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_fire_protection_instance

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | int | no/depends |  |
| typeId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_fire_protection_instance",
  "params": {
    "levelId": 0,
    "typeId": 0
  }
}
```

## Related
- get_fire_protection_instances
- move_fire_protection_instance
- delete_fire_protection_instance
- get_fire_protection_parameters
- set_fire_protection_parameter
- get_fire_protection_types
- duplicate_fire_protection_type
- delete_fire_protection_type

### Params Schema
```json
{
  "type": "object",
  "properties": {
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
