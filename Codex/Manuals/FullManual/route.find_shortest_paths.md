# route.find_shortest_paths

- Category: Route
- Purpose: Find shortest travel polylines in a plan view (analysis-only; does not create elements).

## Overview
Wraps `Autodesk.Revit.DB.Analysis.PathOfTravel.FindShortestPaths(...)`.

- Works only in a **plan view** (`ViewPlan`: Floor Plan / Ceiling Plan).
- Uses the **visible geometry** of the view as obstacles (crop/visibility can affect results).
- Returns polylines as point lists (mm) and their lengths (meters).

## Usage
- Method: `route.find_shortest_paths`
- Transaction: Read

### Parameters
You can call it in either of these forms:

1) Single start/end
| Name | Type | Required |
|---|---|---:|
| viewId | int | no |
| start | point | yes |
| end | point | yes |

2) Multiple starts/ends (best-effort)
| Name | Type | Required |
|---|---|---:|
| viewId | int | no |
| starts | point[] | yes |
| ends | point[] | yes |

Point format (mm):
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

Notes:
- Z is snapped to the view level elevation.
- This command does not create `PathOfTravel` elements.

### Example Request (single)
```json
{
  "jsonrpc": "2.0",
  "id": "route-1",
  "method": "route.find_shortest_paths",
  "params": {
    "viewId": 123456,
    "start": { "x": 1000, "y": 1000, "z": 0 },
    "end": { "x": 9000, "y": 1000, "z": 0 }
  }
}
```

## Result
- `ok`: boolean
- `data.items[]`: `{ index, lengthM, points[] }`
- `data.bestIndex`, `data.bestLengthM`

If no route is found, the command returns `ok:false` and `code=NO_PATH` with `nextActions[]`.

