# view_filter.apply_to_view

- Category: ViewFilterOps
- Purpose: Apply a filter to a view/template with overrides.

## Overview
Applies a filter to a view (or view template) and optionally sets:
- enabled state
- visibility state
- `OverrideGraphicSettings` (subset: halftone/transparency/line color+weight)

If the target view has a View Template applied, operations may be locked. Use `detachViewTemplate=true` or target the template view.

## Usage
- Method: view_filter.apply_to_view

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| application | object | yes |  |
| detachViewTemplate | bool | no | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.apply_to_view",
  "params": {
    "detachViewTemplate": false,
    "application": {
      "view": { "elementId": 12345 },
      "filter": { "name": "A-WALLS-RC" },
      "enabled": true,
      "visible": true,
      "overrides": {
        "transparency": 0,
        "projectionLine": { "color": "#FF0000", "weight": 5 }
      }
    }
  }
}
```

## Related
- view_filter.remove_from_view
- view_filter.get_order
- view_filter.set_order

