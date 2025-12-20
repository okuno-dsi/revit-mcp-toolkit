# update_level_name

- Category: LevelOps
- Purpose: Update Level Name in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_level_name

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | long | no/depends |  |
| name | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_level_name",
  "params": {
    "levelId": "...",
    "name": "..."
  }
}
```

## Related
- create_level
- get_levels
- update_level_elevation
- get_level_parameters
- update_level_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelId": {},
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
