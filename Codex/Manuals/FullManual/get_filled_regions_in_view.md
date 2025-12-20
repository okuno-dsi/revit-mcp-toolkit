# get_filled_regions_in_view

- Category: Annotations/FilledRegion
- Purpose: List FilledRegion instances in a given view.

## Overview
Returns all `FilledRegion` elements in a specified view (or the active view if no viewId is provided).  
Each item carries element id, unique id, type id, and basic flags such as `isMasking`.

## Usage
- Method: get_filled_regions_in_view

### Parameters
```jsonc
{
  "viewId": 11120738   // optional; if omitted, uses the active view
}
```

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_filled_regions_in_view",
  "params": {
    "viewId": 11120738
  }
}
```

## Result
```jsonc
{
  "ok": true,
  "viewId": 11120738,
  "totalCount": 3,
  "items": [
    {
      "elementId": 600001,
      "uniqueId": "....",
      "typeId": 12345,
      "viewId": 11120738,
      "isMasking": false
    }
  ]
}
```

## Related
- get_filled_region_types
- create_filled_region
- delete_filled_region

