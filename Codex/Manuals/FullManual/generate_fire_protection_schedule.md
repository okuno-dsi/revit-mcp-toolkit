# generate_fire_protection_schedule

- Category: FireProtection
- Purpose: Generate Fire Protection Schedule in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: generate_fire_protection_schedule

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| title | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "generate_fire_protection_schedule",
  "params": {
    "title": "..."
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
- duplicate_fire_protection_type

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "title": {
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
