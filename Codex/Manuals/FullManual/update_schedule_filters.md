# update_schedule_filters

- Category: ScheduleOps
- Purpose: Update Schedule Filters in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: update_schedule_filters

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| scheduleViewId | int | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_schedule_filters",
  "params": {
    "scheduleViewId": 0
  }
}
```

## Related
- get_schedules
- create_schedule_view
- get_schedule_data
- list_schedulable_fields
- update_schedule_fields
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
