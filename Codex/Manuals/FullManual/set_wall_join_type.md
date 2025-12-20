# set_wall_join_type / set_wall_miter_joint

- Category: ElementOps / Wall
- Purpose: Set the wall join type (e.g., Miter / Butt) at one or both ends of wall elements.

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It is intended to control how walls meet at their ends.  
However, in the Revit build used for this add-in, the public API does **not** expose `WallJoinType` / `WallUtils.SetWallJoinType`, so the command currently returns `ok:false` with a message indicating that wall join type editing is not available via MCP.  
The alias `set_wall_miter_joint` is reserved for future use and behaves the same (returns an error) in this build.

## Usage
- Method: `set_wall_join_type`  
  or alias: `set_wall_miter_joint`

### Parameters
| Name       | Type   | Required           | Default     |
|------------|--------|--------------------|-------------|
| elementIds | int[]  | no / one of        | []          |
| elementId  | int    | no / one of        | 0           |
| uniqueId   | string | no / one of        |             |
| joinType   | string | no (see below)     | (alias-based) |
| endIndex   | int    | no                 | (see below) |
| ends       | int[]  | no                 | (see below) |

- You must specify at least one of `elementIds`, `elementId`, or `uniqueId`.
- `joinType`:
  - `"Miter"` or `"Butt"` (case-insensitive).
  - If omitted and the method name is `set_wall_miter_joint`, `"Miter"` is assumed.
- `endIndex` / `ends`:
  - Valid values are `0` or `1` (wall start / end).
  - If neither is provided, both ends `[0, 1]` are used.

### Example Request (explicit join type)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_join_type",
  "params": {
    "elementIds": [12345, 67890],
    "joinType": "Miter",
    "ends": [0, 1]
  }
}
```

### Example Request (alias for miter)
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_wall_miter_joint",
  "params": {
    "elementId": 12345,
    "endIndex": 0
  }
}
```

### Example Result (when supported)
```json
{
  "ok": true,
  "requested": 2,
  "processed": 2,
  "changed": 2,
  "notWall": 0,
  "noJoin": 0,
  "failed": 0,
  "joinType": "Miter"
}
```

## Notes
- In the current Revit version/build targeted by this add-in, wall join type edits are **not** performed; the command acts as a stub and always returns `ok:false` with an explanatory message.
- When/if the API exposes a safe, public way to set wall join types, this command can be wired up to actually change joins (e.g., Miter / Butt).
- For now, adjust wall join types directly in the Revit UI, and use:
  - `disallow_wall_join_at_end` to manage which ends are allowed to join,
  - `get_joined_elements` / `unjoin_elements` to manage geometry joins.

## Related
- disallow_wall_join_at_end
- get_joined_elements
- unjoin_elements
