# get_selected_element_ids

- Category: Misc
- Purpose: Get selected elementIds and classify whether selection is from model or Project Browser.

## Overview
Returns selected elementIds (ints), stash source, and classification fields:
- `selectionKind`: `Model` / `ProjectBrowser` / `Mixed` / `None` / `ProjectBrowserNonElementOrNone`
- `browserElementIds`, `modelElementIds`, `missingElementIds`
- `selectionSource` (`live` or `stash`)

## Usage
- Method: `get_selected_element_ids`
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
  "method": "get_selected_element_ids",
  "params": {}
}
```

## Result (example)
```jsonc
{
  "ok": true,
  "elementIds": [1234567, 1234568],
  "count": 2,
  "selectionKind": "Model",
  "isProjectBrowserActive": false,
  "browserElementIds": [],
  "modelElementIds": [1234567, 1234568],
  "missingElementIds": [],
  "classificationCounts": { "browser": 0, "model": 2, "missing": 0 },
  "source": "live",
  "selectionSource": "live",
  "docKey": "xxxx",
  "activeViewId": 890123
}
```

Notes:
- If nothing is selected, `count=0` and `elementIds=[]`.
- In Project Browser, rows without backing element IDs are not returned as model elements.
- To inspect browser-side breakdown, use `get_project_browser_selection`.

## Related
- get_project_browser_selection
- stash_selection
- restore_selection
- get_element_info
