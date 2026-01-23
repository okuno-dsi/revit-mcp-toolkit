# view_filter.remove_from_view

- Category: ViewFilterOps
- Purpose: Remove a filter from a view/template.

## Overview
Removes a filter application from a view (or view template).
If the view has a View Template applied, operations may be locked unless `detachViewTemplate=true`.

## Usage
- Method: view_filter.remove_from_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| view | object | yes |  |
| filter | object | yes |  |
| detachViewTemplate | bool | no | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.remove_from_view",
  "params": {
    "view": { "elementId": 12345 },
    "filter": { "name": "A-WALLS-RC" },
    "detachViewTemplate": false
  }
}
```

## Related
- view_filter.apply_to_view
- view_filter.get_order
- view_filter.set_order

