# rebar_layout_update_by_host

- Category: Rebar
- Purpose: Collect rebars hosted by selected host elements (columns/beams/etc.) and update their layout in bulk.

## Usage
- Method: `rebar_layout_update_by_host`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | Host element ids (must be valid rebar hosts). |
| useSelectionIfEmpty | bool | no | true | If true and `hostElementIds` is empty, use current selection. |
| layout | object | yes |  | Same layout spec as `rebar_layout_update`. |
| filter | object | no |  | Optional filter. |
| filter.commentsTagEquals | string | no |  | If set, only updates rebars whose `Comments` equals this string (case-insensitive, trimmed). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_update_by_host",
  "params": {
    "useSelectionIfEmpty": true,
    "layout": {
      "rule": "fixed_number",
      "numberOfBarPositions": 10,
      "arrayLengthMm": 2500.0,
      "barsOnNormalSide": true,
      "includeFirstBar": true,
      "includeLastBar": true
    }
  }
}
```

## Notes
- Each `hostElementId` is validated via `RebarHostData.IsValidHost`.
- Returns:
  - `updatedRebarIds[]`
  - `skipped[]` (host-level and rebar-level reasons may appear)

## Related
- `rebar_layout_update`
- `rebar_layout_inspect`

