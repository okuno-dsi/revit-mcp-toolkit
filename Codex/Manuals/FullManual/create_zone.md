# create_zone

- Category: ZoneOps
- Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_zone

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| levelId | int | no/depends |  |
| levelName | string | no/depends |  |
| name | string | no/depends |  |
| number | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_zone",
  "params": {
    "levelId": 0,
    "levelName": "...",
    "name": "...",
    "number": "..."
  }
}
```

## Related
- get_zones
- add_spaces_to_zone
- remove_spaces_from_zone
- delete_zone
- list_zone_members
- get_zone_params
- set_zone_param
- compute_zone_metrics

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "levelName": {
      "type": "string"
    },
    "number": {
      "type": "string"
    },
    "name": {
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
