# rebar_layout_inspect

- Category: Rebar
- Purpose: Inspect layout settings of selected Rebar elements (shape-driven set only).

## Usage
- Method: `rebar_layout_inspect`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | Rebar element ids to inspect. |
| elementIds | int[] | no |  | Alias input for convenience. |
| useSelectionIfEmpty | bool | no | true | If true and `rebarElementIds` is empty, use current selection. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rebar_layout_inspect",
  "params": {
    "useSelectionIfEmpty": true
  }
}
```

## Response Notes
- `rebars[]` returns per-id info:
  - `layoutRule` (Revit API enum string)
  - `includeFirstBar`, `includeLastBar`, `numberOfBarPositions`
  - `maxSpacingMm` (if available)
  - `isShapeDriven` (false for free-form or non-rebar)
  - `barsOnNormalSide`, `arrayLengthMm`, `spacingMm` (shape-driven only; best-effort via accessor/reflection)

## Related
- `rebar_layout_update`
- `rebar_layout_update_by_host`
- `rebar_mapping_resolve`
