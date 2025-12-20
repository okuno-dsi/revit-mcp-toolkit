# get_door_window_types_for_schedule

- Category: AnnotationOps
- Purpose: List Door/Window FamilySymbols grouped by Type Mark (for schedules/legends).

## Overview
Scans Door/Window `FamilySymbol`s and returns type metadata keyed by a “mark” parameter (defaults to `Type Mark`).

## Usage
- Method: `get_door_window_types_for_schedule`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| categories | string[] | no | (Doors+Windows) |
| markParameter | string | no | Type Mark |
| onlyUsedInModel | bool | no | false |

- `categories`: e.g. `["Doors","Windows"]`. If provided, only listed categories are included.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_door_window_types_for_schedule",
  "params": {
    "categories": ["Doors", "Windows"],
    "markParameter": "Type Mark",
    "onlyUsedInModel": true
  }
}
```

## Related
- create_door_window_legend_by_mark

