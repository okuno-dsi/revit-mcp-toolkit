# disallow_wall_join_at_end

- Category: ElementOps / Wall
- Purpose: Disallow wall joins at one or both ends of walls.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It wraps `WallUtils.DisallowWallJoinAtEnd` to turn off joins at specified ends of wall elements, typically as a pre-step before moving or editing walls without triggering join-related dialogs.

## Usage
- Method: `disallow_wall_join_at_end`

### Parameters
| Name       | Type   | Required           | Default     |
|------------|--------|--------------------|-------------|
| elementIds | int[]  | no / one of        | []          |
| elementId  | int    | no / one of        | 0           |
| uniqueId   | string | no / one of        |             |
| endIndex   | int    | no                 | (see below) |
| ends       | int[]  | no                 | (see below) |

- You must specify at least one of `elementIds`, `elementId`, or `uniqueId`.
- `endIndex` / `ends`:
  - Valid values are `0` or `1` (wall start / end).
  - If neither is provided, both ends `[0, 1]` are used.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "disallow_wall_join_at_end",
  "params": {
    "elementIds": [12345, 67890],
    "ends": [0, 1]
  }
}
```

### Example Result
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changedEnds": 4,
  "skipped": 0,
  "notWall": 0,
  "failed": 0
}
```

## Notes
- Only `Wall` elements are processed; other element types are counted under `notWall`.
- Use this before moving walls if you want to avoid join/connection dialogs popping up in Revit.
- This does **not** remove existing geometry joins; combine with:
  - `get_joined_elements` + `unjoin_elements` for geometry joins,
  - then `disallow_wall_join_at_end` to prevent new joins when moving walls.

## Related
- set_wall_join_type
- set_wall_miter_joint
- get_joined_elements
- unjoin_elements
