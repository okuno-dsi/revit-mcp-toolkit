# set_wall_top_to_overhead

- Category: Walls
- Purpose: Set a wall's top to the nearest overhead element (Floors/Roofs/Ceilings/Framing) using raycast, with optional attach for Floors/Roofs.

## Overview
This command raycasts upward from several sample points along a wall to find the dominant overhead element. In `mode:"auto"` it tries to attach the wall top when the dominant element is a Floor/Roof (best for sloped roofs). Otherwise it sets `WALL_TOP_OFFSET` (or unconnected height as fallback) based on the ray hit Z.

Notes:
- Requires a non-template, non-perspective 3D view (uses `{3D}` when available). If none exists and `apply:true`, it may create a temporary 3D view.
- For sloped roofs/floors: prefer `mode:"auto"` or `mode:"attach"` to avoid “short wall” caused by using a global bbox minZ.
- If the wall might already be higher than the target (e.g., trimming a wall down to a Ceiling), set `startFrom:"wallBase"` (or `startFrom:"base"`) so the ray starts below the target.

## Usage
- Method: set_wall_top_to_overhead

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| wallId | int | no/depends | (selection) |
| wallIds | int[] | no/depends | (selection) |
| elementId | int | no/depends | (alias of wallId) |
| elementIds | int[] | no/depends | (alias of wallIds) |
| mode | string | no | auto |
| categoryIds | int[] | no | Floors/Roofs/Ceilings/StructuralFraming |
| categoryNames | string[] | no | (optional alternative to categoryIds) |
| view3dId | int | no | auto-detect |
| includeLinked | bool | no | false |
| sampleFractions | number[] | no | [0.1,0.3,0.5,0.7,0.9] |
| sampleCount | int | no | (ignored when sampleFractions is provided) |
| startFrom | string | no | wallTop |
| startBelowTopMm | number | no | 10 |
| maxDistanceMm | number | no | 50000 |
| zAggregate | string | no | max |
| apply | bool | no | true |
| dryRun | bool | no | false |
| keepTempView | bool | no | false |

### Example (selected wall, default behavior)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_top_to_overhead",
  "params": {}
}
```

### Example (raycast only, limited targets)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_top_to_overhead",
  "params": {
    "mode": "raycast",
    "categoryIds": [-2000032, -2000035],
    "zAggregate": "max",
    "dryRun": true
  }
}
```

## Related
- update_wall_parameter
- get_wall_baseline
- get_bounding_box
- create_section
