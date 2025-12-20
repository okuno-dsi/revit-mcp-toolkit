# export_schedule_to_excel

- Category: ScheduleOps
- Purpose: Export a ViewSchedule to .xlsx using ClosedXML (no Excel required)

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_schedule_to_excel

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| autoFit | bool | no/depends | true |
| filePath | string | no/depends |  |
| viewId | int | no/depends |  |
| viewName | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_schedule_to_excel",
  "params": {
    "autoFit": false,
    "filePath": "...",
    "viewId": 0,
    "viewName": "..."
  }
}
```

## Related
- get_schedules
- create_schedule_view
- get_schedule_data
- list_schedulable_fields
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
- export_schedule_to_csv

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "autoFit": {
      "type": "boolean"
    },
    "viewId": {
      "type": "integer"
    },
    "viewName": {
      "type": "string"
    },
    "filePath": {
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
