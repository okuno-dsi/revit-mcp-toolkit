# egress.create_waypoint_guided_path

- Category: Route
- Purpose: Create a waypoint-guided egress route using Revit PathOfTravel.

## Overview
Creates a `PathOfTravel` element in a plan view and optionally applies **2–5 waypoints** to force a non-shortest route.

Two modes are supported:
- `roomMostRemoteToDoor`: compute a start point inside a room (most remote from a specified room door), then route to a target door.
- `pointToDoor`: route from an explicit start point to a target door.

## Usage
- Method: `egress.create_waypoint_guided_path`
- Transaction: Write
- Aliases: `revit.egress.createWaypointGuided`, `egress.create_waypoint_guided`

## Parameters
| Name | Type | Required | Default |
|---|---|---:|---|
| viewId | int | no | ActiveView |
| mode | string | yes | `"roomMostRemoteToDoor"` |
| targetDoorId | int | yes |  |
| waypoints | point[] | no | [] |
| clearanceMm | number | no | 450 |
| doorApproachOffsetMm | number | no | `clamp(clearanceMm, 200..600)` |
| createLabel | bool | no | false |
| labelFormat | string | no | `"{len_m:0.00} m"` |
| labelOffsetMm | number | no | 300 |
| createTag | bool | no | false |
| tagTypeId | int | no |  |
| tagTypeFromSelection | bool | no | true |
| tagAddLeader | bool | no | true |
| tagOrientation | string | no | `"Horizontal"` |
| tagOffsetMm | number | no | 300 |
| forcePassThroughRoomDoor | bool | no | true |
| gridSpacingMm | number | no | 600 |
| maxSamples | int | no | 2000 |
| maxSolverCandidates | int | no | 200 |

Mode-specific parameters:

### mode = `roomMostRemoteToDoor`
| Name | Type | Required |
|---|---|---:|
| roomId | int | yes |
| roomDoorId | int | yes |

### mode = `pointToDoor`
| Name | Type | Required |
|---|---|---:|
| start | point | yes |

Point format (mm):
```json
{ "x": 0.0, "y": 0.0, "z": 0.0 }
```

Notes:
- Works only in `ViewPlan` (Floor Plan / Ceiling Plan).
- Z is snapped to the view’s level elevation.
- `PathOfTravel` uses **visible geometry** as obstacles; crop/visibility affects results.
- When `createTag=true`, the command tries to create a PathOfTravel tag. If `tagTypeId` is not provided and no tag is selected, it returns `ok:false` and stops.

## Example Request (roomMostRemoteToDoor)
```json
{
  "jsonrpc": "2.0",
  "id": "egress-1",
  "method": "egress.create_waypoint_guided_path",
  "params": {
    "viewId": 123456,
    "mode": "roomMostRemoteToDoor",
    "roomId": 456789,
    "roomDoorId": 111222,
    "targetDoorId": 333444,
    "clearanceMm": 450,
    "waypoints": [
      { "x": 2000, "y": 3000, "z": 0 },
      { "x": 6000, "y": 3000, "z": 0 }
    ],
    "createTag": true,
    "tagTypeFromSelection": true,
    "createLabel": true,
    "labelFormat": "{len_m:0.00} m",
    "labelOffsetMm": 300
  }
}
```

## Result
- `data.pathId`: created `PathOfTravel` element id
- `data.statusCreate`: `PathOfTravelCalculationStatus` from `Create(...)`
- `data.status`: `PathOfTravelCalculationStatus` after applying waypoints and calling `Update()`
- `data.lengthM`: route length (meters)
- `data.startPoint`, `data.endPoint`: used points (mm)
- `data.waypointsApplied`: count
- `data.labelId`: optional TextNote id (0 if not created)
- `data.debug.roomMostRemote`: present in `roomMostRemoteToDoor` mode (sampling stats)
- `warnings[]`: non-fatal notes (e.g., crop affected, label failure)
