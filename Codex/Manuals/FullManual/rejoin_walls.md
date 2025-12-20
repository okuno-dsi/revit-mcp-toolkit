# rejoin_walls

- Category: ElementOps / Wall
- Purpose: Rejoin multiple walls by allowing join at ends, setting wall join type (Butt/Miter/SquareOff) when possible, and optionally rejoining geometry at wall joins.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It operates on one or more wall instances and:

- Calls `WallUtils.AllowWallJoinAtEnd(w, 0/1)` to re-allow joins at both ends.
- Sets `LocationCurve.JoinType[end]` to the requested join type (when a wall join exists).
- Optionally reorders join priority (when a preferred wall is specified for Butt/SquareOff).
- Optionally re-runs `JoinGeometryUtils.JoinGeometry` for neighbors at wall joins.

It is intended as a higher-level “rejoin walls” helper that approximates Revit UI’s wall join behaviors.

## Usage
- Method: `rejoin_walls`

### Parameters
| Name            | Type    | Required        | Default |
|-----------------|---------|-----------------|---------|
| wall_ids        | int[]   | yes             | []      |
| elementIds      | int[]   | no / alias      | []      |
| join_type       | string  | no              | `"butt"`|
| prefer_wall_id  | int     | no              | null    |
| also_join_geometry | bool | no              | false   |

- `wall_ids` and `elementIds` are aliases; at least one of them must contain wall element IDs.
- `join_type`:
  - `"butt"` → `JoinType.Abut`
  - `"miter"` → `JoinType.Miter`
  - `"square"` / `"squareoff"` → `JoinType.SquareOff`
  - Omitted or unknown → treated as `"butt"`.
- `prefer_wall_id` (optional):
  - For Butt / SquareOff, this wall will be moved to the front of `LocationCurve.ElementsAtJoin[end]` where present, approximating “Switch Join Order” at that end.
- `also_join_geometry`:
  - When true, the command iterates `LocationCurve.ElementsAtJoin[end]` and calls `JoinGeometryUtils.JoinGeometry(doc, wall, neighbor)` for each neighbor, re-establishing geometry joins at that junction.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rejoin_walls",
  "params": {
    "wall_ids": [6798115, 6798116],
    "join_type": "miter",
    "prefer_wall_id": 6798115,
    "also_join_geometry": true
  }
}
```

### Example Result
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "notWall": 0,
  "noLocation": 0,
  "failed": 0,
  "joinType": "Miter",
  "preferWallId": null,
  "alsoJoinGeometry": true,
  "joinTypeSetCount": 3,
  "reorderCount": 0,
  "geometryJoinCount": 0,
  "results": [
    {
      "elementId": 6798115,
      "status": "ok",
      "joinTypeSetAtEnds": 2,
      "reorderedEnds": 0,
      "geometryJoins": 0
    },
    {
      "elementId": 6798116,
      "status": "ok",
      "joinTypeSetAtEnds": 1,
      "reorderedEnds": 0,
      "geometryJoins": 0
    }
  ]
}
```

## Notes
- Only walls (`Autodesk.Revit.DB.Wall`) are processed; other element IDs contribute to `notWall`.
- `noLocation` counts walls without a `LocationCurve` (rare for standard walls).
- `joinTypeSetCount` is the total number of end-points where `JoinType` was successfully set; not all combinations of join type and geometry are supported by Revit, so some ends may remain unchanged.
- `geometryJoinCount` is the number of neighbor joins where `JoinGeometryUtils.JoinGeometry` completed without error.

## Related
- disallow_wall_join_at_end
- join_elements
- unjoin_elements
