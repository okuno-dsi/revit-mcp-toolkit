# list_schedulable_fields

- Category: ScheduleOps
- Purpose: List Schedulable Fields in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: list_schedulable_fields

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categoryName | string | no/depends |  |
| scheduleViewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_schedulable_fields",
  "params": {
    "categoryName": "...",
    "scheduleViewId": 0
  }
}
```

## Related
- get_schedules
- create_schedule_view
- get_schedule_data
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
- export_schedule_to_csv
- delete_schedule

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "scheduleViewId": {
      "type": "integer"
    },
    "categoryName": {
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
