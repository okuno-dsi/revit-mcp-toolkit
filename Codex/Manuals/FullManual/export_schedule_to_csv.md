# export_schedule_to_csv

- Category: ScheduleOps
- Purpose: Export Schedule To Csv in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: export_schedule_to_csv

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| delimiter | string | no/depends |  |
| fillBlanks | bool | no/depends | false |
| includeHeader | bool | no/depends | true |
| itemize | bool | no/depends | false |
| newline | string | no/depends |  |
| outputFilePath | string | no/depends |  |
| scheduleViewId | int | no/depends | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_schedule_to_csv",
  "params": {
    "delimiter": "...",
    "fillBlanks": false,
    "includeHeader": false,
    "itemize": false,
    "newline": "...",
    "outputFilePath": "...",
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
- update_schedule_filters
- update_schedule_sorting
- delete_schedule

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "outputFilePath": {
      "type": "string"
    },
    "fillBlanks": {
      "type": "boolean"
    },
    "newline": {
      "type": "string"
    },
    "includeHeader": {
      "type": "boolean"
    },
    "scheduleViewId": {
      "type": "integer"
    },
    "itemize": {
      "type": "boolean"
    },
    "delimiter": {
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
