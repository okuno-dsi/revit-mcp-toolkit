# export_view_3dm_brep

- Category: Export
- Purpose: Export View 3Dm Brep in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_view_3dm_brep

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| angTolDeg | number | no/depends | 1.0 |
| curveTol | number | no/depends | 1 |
| includeLinked | bool | no/depends | true |
| layerMode | string | no/depends | byCategory |
| outPath | string | no/depends |  |
| unitsOut | string | no/depends | mm |
| viewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_view_3dm_brep",
  "params": {
    "angTolDeg": 0.0,
    "curveTol": 0.0,
    "includeLinked": false,
    "layerMode": "...",
    "outPath": "...",
    "unitsOut": "...",
    "viewId": 0
  }
}
```

## Related
- export_dashboard_html
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeLinked": {
      "type": "boolean"
    },
    "angTolDeg": {
      "type": "number"
    },
    "layerMode": {
      "type": "string"
    },
    "curveTol": {
      "type": "number"
    },
    "outPath": {
      "type": "string"
    },
    "unitsOut": {
      "type": "string"
    },
    "viewId": {
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
