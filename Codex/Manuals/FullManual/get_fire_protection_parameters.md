# get_fire_protection_parameters

- Category: FireProtection
- Purpose: Get Fire Protection Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_fire_protection_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no/depends |  |
| includeDisplay | bool | no/depends | true |
| includeRaw | bool | no/depends | true |
| includeUnit | bool | no/depends | true |
| siDigits | int | no/depends | 3 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_fire_protection_parameters",
  "params": {
    "elementId": 0,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "siDigits": 0
  }
}
```

## Related
- get_fire_protection_instances
- create_fire_protection_instance
- move_fire_protection_instance
- delete_fire_protection_instance
- set_fire_protection_parameter
- get_fire_protection_types
- duplicate_fire_protection_type
- delete_fire_protection_type

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeDisplay": {
      "type": "boolean"
    },
    "elementId": {
      "type": "integer"
    },
    "includeUnit": {
      "type": "boolean"
    },
    "includeRaw": {
      "type": "boolean"
    },
    "siDigits": {
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
