# export_dwg_by_param_groups

- Category: Export
- Purpose: Export Dwg By Param Groups in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_dwg_by_param_groups

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| asyncMode | bool | no/depends | true |
| maxMillisPerPass | int | no/depends | 20000 |
| sessionKey | string | no/depends | OST_Walls |
| startIndex | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg_by_param_groups",
  "params": {
    "asyncMode": false,
    "maxMillisPerPass": 0,
    "sessionKey": "...",
    "startIndex": 0
  }
}
```

## Related
- export_dashboard_html
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "asyncMode": {
      "type": "boolean"
    },
    "startIndex": {
      "type": "integer"
    },
    "sessionKey": {
      "type": "string"
    },
    "maxMillisPerPass": {
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
