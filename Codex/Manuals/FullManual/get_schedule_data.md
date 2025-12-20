# get_schedule_data

- Category: ScheduleOps
- Purpose: Get Schedule Data in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: get_schedule_data

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| count | int | no/depends | 1000 |
| scheduleViewId | int | no/depends |  |
| skip | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_schedule_data",
  "params": {
    "count": 0,
    "scheduleViewId": 0,
    "skip": 0
  }
}
```

## Related
- get_schedules
- create_schedule_view
- list_schedulable_fields
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
    "skip": {
      "type": "integer"
    },
    "scheduleViewId": {
      "type": "integer"
    },
    "count": {
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
