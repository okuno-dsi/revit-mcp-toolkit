# get_levels

- Category: LevelOps
- Purpose: Get Levels in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_levels

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_levels",
  "params": {
    "count": 0,
    "skip": 0
  }
}
```

## Related
- create_level
- update_level_name
- update_level_elevation
- get_level_parameters
- update_level_parameter

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "count": {
      "type": "integer"
    },
    "skip": {
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
