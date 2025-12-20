# export_view_3dm

- Category: Export
- Purpose: Export View 3Dm in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_view_3dm

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| detailLevel | string | no/depends | Fine |
| includeLinked | bool | no/depends | true |
| layerMode | string | no/depends | byCategory |
| outPath | string | no/depends |  |
| unitsOut | string | no/depends | mm |
| viewId | int | no/depends | 0 |
| weld | bool | no/depends | true |
| weldTolerance | number | no/depends | 1 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_view_3dm",
  "params": {
    "detailLevel": "...",
    "includeLinked": false,
    "layerMode": "...",
    "outPath": "...",
    "unitsOut": "...",
    "viewId": 0,
    "weld": false,
    "weldTolerance": 0.0
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
- export_view_3dm_brep

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "includeLinked": {
      "type": "boolean"
    },
    "layerMode": {
      "type": "string"
    },
    "weld": {
      "type": "boolean"
    },
    "detailLevel": {
      "type": "string"
    },
    "weldTolerance": {
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
