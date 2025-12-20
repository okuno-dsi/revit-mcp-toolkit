# get_level_parameters

- Category: LevelOps
- Purpose: Get Level Parameters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_level_parameters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_level_parameters",
  "params": {
    "levelId": 0
  }
}
```

## Related
- create_level
- get_levels
- update_level_name
- update_level_elevation
- update_level_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
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
