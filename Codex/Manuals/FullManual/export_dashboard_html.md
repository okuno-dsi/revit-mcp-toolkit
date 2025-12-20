# export_dashboard_html

- Category: Export
- Purpose: Export Dashboard Html in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_dashboard_html
- Parameters: none

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_dashboard_html",
  "params": {}
}
```

## Related
- export_schedules_html
- export_dwg
- export_dwg_with_workset_bucketing
- export_dwg_by_param_groups
- export_view_mesh
- export_view_3dm
- export_view_3dm_brep

### Params Schema
```json
{
  "type": "object",
  "properties": {}
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
