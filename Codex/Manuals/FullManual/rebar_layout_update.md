# rebar_layout_update

- Category: Rebar
- Purpose: Update layout rule/spacing/count settings for selected Rebar elements (shape-driven set only).

## Usage
- Method: `rebar_layout_update` (alias: `rebar_arrangement_update`)

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| rebarElementIds | int[] | yes |  | Target rebar ids. |
| elementIds | int[] | no |  | Alias input for convenience. |
| useSelectionIfEmpty | bool | no | false | If true and `rebarElementIds` is empty, use current selection. |
| layout | object | yes |  | Layout spec (see below). |

### Layout spec
```json
{
  "rule": "maximum_spacing",
  "spacingMm": 150.0,
  "arrayLengthMm": 2900.0,
  "numberOfBarPositions": null,
  "barsOnNormalSide": true,
  "includeFirstBar": true,
  "includeLastBar": true
}
```

Supported `rule` values:
- `single`
- `fixed_number`
- `maximum_spacing`
- `number_with_spacing`
- `minimum_clear_spacing` (optional; best-effort via reflection if API is available)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_update",
  "params": {
    "rebarElementIds": [1234567],
    "layout": {
      "rule": "maximum_spacing",
      "spacingMm": 150.0,
      "arrayLengthMm": 2900.0,
      "barsOnNormalSide": true,
      "includeFirstBar": true,
      "includeLastBar": true
    }
  }
}
```

## Notes
- Free-form rebars are skipped with `NOT_SHAPE_DRIVEN`.
- Returns:
  - `updatedRebarIds[]`
  - `skipped[]` with `reason` and optional `msg`

## Related
- `rebar_layout_inspect`
- `rebar_layout_update_by_host`
