# list_zone_members

- Category: ZoneOps
- Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_zone_members

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| zoneId | int | no/depends |  |
| zoneUniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_zone_members",
  "params": {
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
- get_zone_params
- set_zone_param
- compute_zone_metrics

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "zoneId": {
      "type": "integer"
    },
    "zoneUniqueId": {
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
