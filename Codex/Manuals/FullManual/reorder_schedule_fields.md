# reorder_schedule_fields

- Category: ScheduleOps
- Purpose: Reorder existing schedule columns (fields) in a ViewSchedule.

## Overview
Revit schedules store column order as the field order in `ScheduleDefinition`. This command reorders **existing** fields using `ScheduleDefinition.SetFieldOrder` (via reflection), avoiding field recreation so existing filters/sorting that reference `ScheduleFieldId` stay valid.

## Usage
- Method: reorder_schedule_fields

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| scheduleViewId | int | no* |  |
| order | array | no* |  |
| moveColumn | string | no* |  |
| beforeColumn | string | no* |  |
| moveIndex | int | no* |  |
| beforeIndex | int | no* |  |
| includeHidden | bool | no | false |
| appendUnspecified | bool | no | true |
| strict | bool | no | false |
| visibleOnly | bool | no | true |
| returnFields | bool | no | *(order-mode: true / move-mode: false)* |

\* Required as a set:
- Either provide `order`, or provide a move pair (`moveColumn`+`beforeColumn` or `moveIndex`+`beforeIndex`).
- `scheduleViewId` can be omitted when the active view is a schedule.

### Fast move mode
- Use `moveColumn` / `beforeColumn` with Excel-like letters (A..Z, AA..). Example: move `N` before `H`.

### `order` items
Each item can be one of:
- **string**: matches by field `name` (`ScheduleField.GetName()`) or `heading` (`ScheduleField.ColumnHeading`)
- **int**: matches by `paramId` (`ScheduleField.ParameterId.IntegerValue`)
- **object**: `{ "paramId": 123 }` or `{ "heading": "..." }` or `{ "name": "..." }` (paramId → heading → name)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reorder_schedule_fields",
  "params": {
    "scheduleViewId": 12345,
    "order": ["Type Mark", "Family and Type", "Comments"],
    "appendUnspecified": true,
    "includeHidden": false,
    "strict": false
  }
}
```

### Example (fast move, active schedule view)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reorder_schedule_fields",
  "params": {
    "moveColumn": "N",
    "beforeColumn": "H"
  }
}
```

## Related
- get_current_view
- get_schedules
- inspect_schedule_fields
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
