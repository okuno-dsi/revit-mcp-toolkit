# create_level

- Category: LevelOps
- Purpose: Create Level in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_level

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elevation | number | no/depends |  |
| name | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_level",
  "params": {
    "elevation": 0.0,
    "name": "..."
  }
}
```

## Related
- get_levels
- update_level_name
- update_level_elevation
- get_level_parameters
- update_level_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "elevation": {
      "type": "number"
    },
    "name": {
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
