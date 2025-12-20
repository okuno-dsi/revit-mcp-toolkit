# get_zone_params

- Category: ZoneOps
- Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_zone_params

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends | 100 |
| desc | bool | no/depends | false |
| includeDisplay | bool | no/depends | true |
| includeRaw | bool | no/depends | true |
| includeUnit | bool | no/depends | true |
| nameContains | string | no/depends |  |
| orderBy | string | no/depends |  |
| skip | int | no/depends | 0 |
| zoneId | int | no/depends |  |
| zoneUniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_zone_params",
  "params": {
    "count": 0,
    "desc": false,
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "nameContains": "...",
    "orderBy": "...",
    "skip": 0,
    "zoneId": 0,
    "zoneUniqueId": "..."
  }
}
```

## Related
- get_zones
- create_zone
- add_spaces_to_zone
- remove_spaces_from_zone
- delete_zone
- list_zone_members
- set_zone_param
- compute_zone_metrics

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "zoneUniqueId": {
      "type": "string"
    },
    "nameContains": {
      "type": "string"
    },
    "skip": {
      "type": "integer"
    },
    "orderBy": {
      "type": "string"
    },
    "includeDisplay": {
      "type": "boolean"
    },
    "desc": {
      "type": "boolean"
    },
    "includeRaw": {
      "type": "boolean"
    },
    "count": {
      "type": "integer"
    },
    "includeUnit": {
      "type": "boolean"
    },
    "zoneId": {
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
