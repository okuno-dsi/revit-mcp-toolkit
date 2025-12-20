# export_dwg

- Category: Export
- Purpose: 2Dビューに「表示されているそのまま」をDWGへ書き出す

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_dwg

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| batchSize | int | no/depends | 10 |
| dwgVersion | string | no/depends |  |
| fileName | string | no/depends |  |
| keepTempView | bool | no/depends | false |
| maxMillisPerTx | int | no/depends | 0 |
| outputFolder | string | no/depends |  |
| startIndex | int | no/depends | 0 |
| useExportSetup | string | no/depends |  |
| viewId | int | no/depends |  |
| viewUniqueId | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dwg",
  "params": {
    "batchSize": 0,
    "dwgVersion": "...",
    "fileName": "...",
    "keepTempView": false,
    "maxMillisPerTx": 0,
    "outputFolder": "...",
    "startIndex": 0,
    "useExportSetup": "...",
    "viewId": 0,
    "viewUniqueId": "..."
  }
}
```

## Related
- export_dashboard_html
- export_schedules_html
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "startIndex": {
      "type": "integer"
    },
    "useExportSetup": {
      "type": "string"
    },
    "outputFolder": {
      "type": "string"
    },
    "keepTempView": {
      "type": "boolean"
    },
    "viewId": {
      "type": "integer"
    },
    "maxMillisPerTx": {
      "type": "integer"
    },
    "batchSize": {
      "type": "integer"
    },
    "fileName": {
      "type": "string"
    },
    "dwgVersion": {
      "type": "string"
    },
    "viewUniqueId": {
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
