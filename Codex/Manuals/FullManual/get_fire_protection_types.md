# get_fire_protection_types

- Category: FireProtection
- Purpose: Get Fire Protection Types in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_fire_protection_types
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_fire_protection_types",
  "params": {}
}
```

## Related
- get_fire_protection_instances
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- get_fire_protection_parameters
- set_fire_protection_parameter
- duplicate_fire_protection_type
- delete_fire_protection_type

### Params Schema
```json
{
  "type": "object",
  "properties": {}
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
