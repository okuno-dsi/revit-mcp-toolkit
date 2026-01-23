# path.waypoints.set

- Category: Route
- Purpose: Replace all waypoints of a `PathOfTravel` element.

## Overview
Replaces the waypoint list of an existing `PathOfTravel` element and (by default) recalculates it.

## Usage
- Method: `path.waypoints.set`
- Transaction: Write

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---|---|
| pathId | int | yes |  | `PathOfTravel` element id |
| waypoints | point[] | yes |  | points are in mm |
| update | bool | no | true | if true, calls `PathOfTravel.Update()` |

Notes:
- `elementId` is also accepted as an alias for `pathId`.
- Z is snapped to the owner view’s level elevation when possible.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "wp-set-1",
  "method": "path.waypoints.set",
  "params": {
    "pathId": 999888,
    "update": true,
    "waypoints": [
      { "x": 2000, "y": 3000, "z": 0 },
      { "x": 4000, "y": 3000, "z": 0 }
    ]
  }
}
```

## Result
- `data.waypointsApplied`: int
- `data.updated`: bool
- `data.status`: string (`PathOfTravelCalculationStatus`)

