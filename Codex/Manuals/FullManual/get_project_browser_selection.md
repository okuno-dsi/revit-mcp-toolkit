# get_project_browser_selection

- Category: Misc
- Purpose: Classify current selection into Project Browser-backed buckets.

## Overview
Returns selected element IDs and classifies them into:
- `families`
- `familyTypes`
- `views`
- `sheets`
- `schedules`
- `others`
- `missing`

It also returns `selectionSource` (`live` or `stash`) for stable automation.

## Usage
- Method: `get_project_browser_selection`
- Parameters (optional):
  - `retry.maxWaitMs` (int)
  - `retry.pollMs` (int)
  - `fallbackToStash` (bool)
  - `maxAgeMs` (int)
  - `allowCrossDoc` (bool)
  - `allowCrossView` (bool)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_project_browser_selection",
  "params": {
    "retry": { "maxWaitMs": 1000, "pollMs": 100 },
    "fallbackToStash": true
  }
}
```

## Result (example)
```jsonc
{
  "ok": true,
  "source": "live",
  "selectionSource": "live",
  "count": 2,
  "families": [],
  "familyTypes": [],
  "views": [{ "elementId": 123, "name": "1FL", "viewType": "FloorPlan" }],
  "sheets": [],
  "schedules": [],
  "others": [],
  "missing": [],
  "counts": {
    "families": 0,
    "familyTypes": 0,
    "views": 1,
    "sheets": 0,
    "schedules": 0,
    "others": 0,
    "missing": 0
  },
  "msg": "OK"
}
```

## Notes
- In Project Browser, rows without backing element IDs cannot be returned as elements.
- Use together with `get_selected_element_ids` when you need both model/browser discrimination and raw selected IDs.

## Related
- get_selected_element_ids
- get_element_info
