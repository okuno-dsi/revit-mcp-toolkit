# inspect_schedule_fields

- Category: ScheduleOps
- Purpose: Inspect Schedule Fields in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: inspect_schedule_fields

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| samplePerField | int | no/depends | 5 |
| scheduleViewId | int | no/depends | 0 |
| title | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "inspect_schedule_fields",
  "params": {
    "samplePerField": 0,
    "scheduleViewId": 0,
    "title": "..."
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
    "samplePerField": {
      "type": "integer"
    },
    "title": {
      "type": "string"
    },
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
