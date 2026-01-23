# element.resolve_spatial_selection

- Category: Spatial
- Purpose: Resolve a Room/Space/Area selection mismatch (or wrong id) to the nearest/containing target spatial element.

## Overview
Use this when:

- You intended to run a **Room** command but selected a **Space** or **Area** (or their tags), or vice versa.
- You have an `elementId` but you are not sure whether it is a Room/Space/Area.

Resolution order (deterministic):
1) Tag unwrap (RoomTag/SpaceTag/AreaTag → underlying element)
2) If already the desired kind → return as-is
3) Containment-first (2D point-in-polygon using boundary segments)
4) Fallback to nearest-by-distance within `maxDistanceMeters`

Notes:
- This command does **not** modify the Revit model. It only returns the resolved spatial element id.
- Many Room/Space/Area commands also auto-correct this internally; use this command to debug/verify.

## Usage
- Method: `element.resolve_spatial_selection` (canonical)
- Legacy alias: `resolve_spatial_selection` (deprecated)

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| elementId | int | no* | null |
| fromSelection | bool | no | false |
| desiredKind | string (`room`/`space`/`area`) | yes |  |
| maxDistanceMeters | number | no | 0.5 |

`*` `elementId` is required unless `fromSelection=true` and a single element is selected.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "fix-1",
  "method": "element.resolve_spatial_selection",
  "params": {
    "fromSelection": true,
    "desiredKind": "room",
    "maxDistanceMeters": 0.5
  }
}
```

## Result
- `ok`: boolean
- `original`: `{ id, kind }` where kind is `room`/`space`/`area`/`unknown`
- `resolved`: `{ id, kind }` where kind is the requested `desiredKind`
- `byContainment`: boolean (true when containment matched)
- `distanceMeters`: number (0 when already correct)
- `msg`: string (human-readable summary)

### Failure behavior
Returns `ok:false` when:
- The selected element id is invalid / not found
- No matching spatial element is found nearby
- Nearest match exists but exceeds `maxDistanceMeters`

