# set_zone_param

- Category: ZoneOps
- Purpose: Revit MEP Zone 操作 一式（9コマンドを1ファイルに集約）

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: set_zone_param

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| paramName | string | no/depends |  |
| zoneId | int | no/depends |  |
| zoneUniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_zone_param",
  "params": {
    "paramName": "...",
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
- get_zone_params
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
    },
    "paramName": {
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
