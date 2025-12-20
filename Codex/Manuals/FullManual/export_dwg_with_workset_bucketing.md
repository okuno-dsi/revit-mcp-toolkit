# export_dwg_with_workset_bucketing

- Category: Export
- Purpose: 「一時ワークセット振替 → 純正DWG出力 → 即ロールバック」

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_dwg_with_workset_bucketing

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| dwgVersion | string | no/depends |  |
| fileName | string | no/depends |  |
| keepTempView | bool | no/depends | false |
| outputFolder | string | no/depends |  |
| unmatchedWorksetName | string | no/depends | WS_UNMATCHED |
| useExportSetup | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg_with_workset_bucketing",
  "params": {
    "dwgVersion": "...",
    "fileName": "...",
    "keepTempView": false,
    "outputFolder": "...",
    "unmatchedWorksetName": "...",
    "useExportSetup": "..."
  }
}
```

## Related
- export_dashboard_html
- export_schedules_html
- export_dwg
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "useExportSetup": {
      "type": "string"
    },
    "outputFolder": {
      "type": "string"
    },
    "keepTempView": {
      "type": "boolean"
    },
    "fileName": {
      "type": "string"
    },
    "unmatchedWorksetName": {
      "type": "string"
    },
    "dwgVersion": {
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
