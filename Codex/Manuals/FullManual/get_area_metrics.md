# get_area_metrics

- Category: Area
- Purpose: Get Area metrics (area m² and perimeter mm) for one or more Area elements.

## Usage
- Method: get_area_metrics

### Parameters (single)
| Name | Type | Required | Default |
|---|---:|:---:|---|
| areaId | int | yes |  |
| boundaryLocation | string | no | `Finish` |

### Parameters (batch)
| Name | Type | Required | Default |
|---|---:|:---:|---|
| areaIds | int[] | yes |  |
| startIndex | int | no | 0 |
| batchSize | int | no | 200 |
| maxMillisPerCall | int | no | 0 (no limit) |
| boundaryLocation | string | no | `Finish` |

Notes:
- Perimeter is computed from `GetBoundarySegments(...)` with `boundaryLocation` (A: calculated boundary). See `Manuals/FullManual/spatial_boundary_location.md`.

## Example (single)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_metrics",
  "params": { "areaId": 11122204, "boundaryLocation": "Finish" }
}
```

## Example (batch)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_area_metrics",
  "params": {
    "areaIds": [11122204, 11122242, 11122243],
    "startIndex": 0,
    "batchSize": 200,
    "maxMillisPerCall": 0,
    "boundaryLocation": "Finish"
  }
}
```

## Result
### Single
- `elementId`, `areaM2`, `perimeterMm`, `level`, `boundaryLocation`

### Batch
- `items`: list of `{ elementId, areaM2, perimeterMm, level }`
- `completed` / `nextIndex`: for paging
- Top-level `boundaryLocation` is included.
