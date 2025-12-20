# create_schedule_view

- Category: ScheduleOps
- Purpose: Create Schedule View in Revit.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It performs the action described in Purpose. Use the Usage section to craft requests.

## Usage
- Method: create_schedule_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| addElementId | bool | no/depends | false |
| categoryName | string | no/depends |  |
| fieldNames | array | no/depends |  |
| fieldParamIds | array | no/depends |  |
| isItemized | bool | no/depends |  |
| title | string | no/depends |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_schedule_view",
  "params": {
    "addElementId": false,
    "categoryName": "...",
    "fieldNames": "...",
    "fieldParamIds": "...",
    "isItemized": false,
    "title": "..."
  }
}
```

## Related
- get_schedules
- get_schedule_data
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
    "fieldNames": {
      "type": "array"
    },
    "isItemized": {
      "type": "boolean"
    },
    "fieldParamIds": {
      "type": "array"
    },
    "addElementId": {
      "type": "boolean"
    },
    "categoryName": {
      "type": "string"
    },
    "title": {
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
