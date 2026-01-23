# view_filter.get_order

- Category: ViewFilterOps
- Purpose: Get filter priority order in a view/template.

## Overview
Returns the actual filter order (`View.GetOrderedFilters()`) for a given view or view template.

## Usage
- Method: view_filter.get_order

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| view | object | yes |  |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.get_order",
  "params": {
    "view": { "elementId": 12345 }
  }
}
```

## Result
- `orderedFilterIds[]`: filter ids in priority order (top → bottom in Revit UI)

## Related
- view_filter.set_order
- view_filter.apply_to_view
- view_filter.remove_from_view

