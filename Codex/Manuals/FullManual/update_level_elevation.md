# update_level_elevation

- Category: LevelOps
- Purpose: Update Level Elevation in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_level_elevation

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elevation | number | no/depends |  |
| levelId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_level_elevation",
  "params": {
    "elevation": 0.0,
    "levelId": 0
  }
}
```

## Related
- create_level
- get_levels
- update_level_name
- get_level_parameters
- update_level_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelId": {
      "type": "integer"
    },
    "elevation": {
      "type": "number"
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
