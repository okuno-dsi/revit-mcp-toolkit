# view_filter.set_order

- Category: ViewFilterOps
- Purpose: Reorder view filters while preserving per-filter state.

## Overview
Reorders filters for a view/template by removing and re-adding them, while preserving:
- enabled state
- visibility state
- `OverrideGraphicSettings`

If the view has a View Template applied, the command may be blocked unless you detach the template (see `detachViewTemplate`).

## Usage
- Method: view_filter.set_order

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| view | object | yes |  |
| orderedFilterIds | int[] | yes |  |
| preserveUnlisted | bool | no | true |
| detachViewTemplate | bool | no | false |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view_filter.set_order",
  "params": {
    "view": { "elementId": 12345 },
    "orderedFilterIds": [67890, 67891, 67892],
    "preserveUnlisted": true,
    "detachViewTemplate": false
  }
}
```

## Result
- `finalOrder[]`: verified order after applying

## Related
- view_filter.get_order
- view_filter.apply_to_view

