# get_zones

- Category: ZoneOps
- Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_zones

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends | 100 |
| levelId | int | no/depends |  |
| levelName | string | no/depends |  |
| nameContains | string | no/depends |  |
| namesOnly | bool | no/depends | false |
| numberContains | string | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_zones",
  "params": {
    "count": 0,
    "levelId": 0,
    "levelName": "...",
    "nameContains": "...",
    "namesOnly": false,
    "numberContains": "...",
    "skip": 0
  }
}
```

## Related
- create_zone
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
    "numberContains": {
      "type": "string"
    },
    "count": {
      "type": "integer"
    },
    "levelId": {
      "type": "integer"
    },
    "skip": {
      "type": "integer"
    },
    "namesOnly": {
      "type": "boolean"
    },
    "nameContains": {
      "type": "string"
    },
    "levelName": {
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
