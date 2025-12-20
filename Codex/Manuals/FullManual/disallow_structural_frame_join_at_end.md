# disallow_structural_frame_join_at_end

- Category: ElementOps / StructuralFrame
- Purpose: Disallow joins at one or both ends of structural framing elements (beams/braces).

## Overview
This command is executed via JSON-RPC against the Revit MCP Add-in. It wraps `StructuralFramingUtils.DisallowJoinAtEnd` to turn off end joins for structural framing members, typically as a pre-step before moving beams without triggering join-related dialogs.

## Usage
- Method: `disallow_structural_frame_join_at_end`

### Parameters
| Name        | Type    | Required           | Default     |
|-------------|---------|--------------------|-------------|
| elementIds  | int[]   | no / one of        | []          |
| elementId   | int     | no / one of        | 0           |
| uniqueId    | string  | no / one of        |             |
| endIndex    | int     | no                 | (see below) |
| ends        | int[]   | no                 | (see below) |

- You must specify at least one of `elementIds`, `elementId`, or `uniqueId`.
- `endIndex` / `ends`:
  - Valid values are `0` or `1` (start/end).
  - If neither is provided, both ends `[0, 1]` are used.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "disallow_structural_frame_join_at_end",
  "params": {
    "elementIds": [5938155, 5938694],
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
  "notFraming": 0,
  "failed": 0
}
```

## Notes
- Only structural framing elements (`FamilyInstance` with `StructuralType` = `Beam` or `Brace`) are processed.
- Use this before moving beams (e.g., via `move_structural_frame`) when you want to avoid join/connection dialogs popping up in Revit.
- This does **not** perform `UnjoinGeometry` itself; combine with:
  - `get_joined_elements` + `unjoin_elements` for geometry joins
  - then `disallow_structural_frame_join_at_end` to prevent new joins when moving.

## Related
- move_structural_frame
- get_joined_elements
- unjoin_elements
- unpin_element
