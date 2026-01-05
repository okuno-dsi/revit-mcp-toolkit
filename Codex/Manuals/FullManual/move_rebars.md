# move_rebars

- Category: Rebar
- Purpose: Move Rebar elements by elementId(s). Alias: `move_rebar`.

## Overview
- Accepts arbitrary element IDs, but moves only “Rebar-like” elements:
  - `Rebar`, `RebarInSystem`, `AreaReinforcement`, `PathReinforcement`
- Offset input supports:
  - `offsetMm: {x,y,z}` (preferred)
  - `dx/dy/dz` (mm)
- Supports `items[]` for per-element offsets, and chunked transactions (`batchSize`).

## Usage
- Method: `move_rebars` (alias: `move_rebar`)

### Parameters (shared-offset form)
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| rebarElementIds | int[] | no |  | Target IDs (preferred). |
| elementIds | int[] | no |  | Alias input for convenience. |
| useSelectionIfEmpty | bool | no | true | If ids are empty, use current selection. |
| offsetMm | object | yes/depends |  | `{x,y,z}` in mm. |
| dx/dy/dz | number | yes/depends |  | Alternative offset in mm. |
| dryRun | bool | no | false | If true, does not move and returns targets. |
| batchSize | int | no | 200 | Chunk size per transaction. |

### Parameters (per-element form)
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| items | object[] | yes/depends |  | Each item requires `elementId` (or `rebarElementId`) + offset. |

### Example Request (shared offset)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_rebars",
  "params": {
    "rebarElementIds": [123456],
    "offsetMm": { "x": 100, "y": 0, "z": 0 }
  }
}
```

## Related
- `delete_rebars`
- `apply_transform_delta` (single element, optional rotate)

