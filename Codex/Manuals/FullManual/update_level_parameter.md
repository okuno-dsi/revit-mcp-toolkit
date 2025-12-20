# update_level_parameter

- Category: LevelOps
- Purpose: Update Level Parameter in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_level_parameter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | int | no/depends |  |
| paramName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_level_parameter",
  "params": {
    "levelId": 0,
    "paramName": "..."
  }
}
```

## Related
- create_level
- get_levels
- update_level_name
- update_level_elevation
- get_level_parameters

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "paramName": {
      "type": "string"
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
