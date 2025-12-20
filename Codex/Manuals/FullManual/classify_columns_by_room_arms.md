# classify_columns_by_room_arms

- Category: Columns
- Purpose: Classify columns as `interior` / `exterior` by sampling “arm endpoints” against Room containment.

## Overview
This command classifies Structural/Architectural columns by checking whether short “arms” cast from the column location are inside any Room (XY-based check, Z is tested near each Room level).

- For each column, the command samples multiple Z heights across the column bounding box.
- At each sampled height, it tests multiple arm directions (±X/±Y plus slight ±1° rotations) to absorb boundary tolerances.
- If `insideCount / totalEndpoints >= 0.9` → `interior`, else → `exterior`.

## Usage
- Method: `classify_columns_by_room_arms`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementIds | int[] | no |  |
| armLengthMm | number | no | 300.0 |
| sampleCount | int | no | 3 |

- `elementIds` (optional): if provided, only these columns are evaluated; otherwise all columns in `OST_StructuralColumns` and `OST_Columns` are evaluated.
- `armLengthMm` is treated as a baseline; the command may automatically increase it to exceed the column width.
- `sampleCount` is clamped to `[1..7]`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "classify_columns_by_room_arms",
  "params": {
    "armLengthMm": 500.0,
    "sampleCount": 3
  }
}
```

## Result (Summary)
- `columns[]`: items include `elementId`, `levelId`, `typeId/typeName`, `totalEndpoints`, `insideCount`, `outsideCount`, and `classification`.

## Related
- get_candidate_exterior_columns
- get_candidate_exterior_walls
- classify_points_in_room

