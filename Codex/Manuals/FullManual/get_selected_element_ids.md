# get_selected_element_ids

- Category: Misc
- Purpose: Get the elementIds of the current UI selection (ActiveUIDocument.Selection).

## Overview
Returns selected elementIds (ints) and basic context (activeViewId, docTitle, etc.).

## Usage
- Method: `get_selected_element_ids`
- Parameters: none

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
  "activeViewId": 890123,
  "docTitle": "MyProject",
  "msg": "OK"
}
```

Notes:
- If nothing is selected, `count=0` and `elementIds=[]`.

## Related
- stash_selection
- restore_selection
- get_element_info

