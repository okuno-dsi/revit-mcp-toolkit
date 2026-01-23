# Apply Finish Wall Type on Room Boundary (segment-length)

- Category: Room
- Kind: write
- Purpose: Create finish/overlay walls along a selected room boundary **segment length** (room side).

Creates new “overlay/finish” walls of a specified `WallType` along the selected `Room` boundary **segment lengths**, on the **room side**.

Use this when the host wall is longer than the room edge (shared by multiple rooms) and you want the finish wall only for the selected room’s edge range.

## Command
- Canonical: `room.apply_finish_wall_type_on_room_boundary`
- Aliases: `apply_finish_wall_type_on_room_boundary`

## Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| newWallTypeNameOrId | string | yes |  | Target `WallType` by name (recommended) or elementId string. |
| roomId | int | no |  | If omitted, uses selection (`fromSelection=true`). |
| fromSelection | bool | no | auto | If true, resolves a selected `Room` as target. |
| boundaryLocation | string | no | Finish | `Finish`/`Center`/`CoreCenter`/`CoreBoundary`. |
| includeIslands | bool | no | false | If true, also processes inner loops (islands). |
| minSegmentLengthMm | number | no | 1.0 | Skips very short boundary segments. |
| onlyRcCore | bool | no | true | If true, only processes boundary segments whose **host wall core layer** matches `coreMaterialNameContains`. |
| coreMaterialNameContains | string | no | *コンクリート | Token(s) to match core layer material names. Split by `,`/`;`/`|`. |
| excludeWallsBetweenRooms | bool | no | false | If true, excludes wall boundary segments when another `Room` is detected on the opposite side (i.e., skips walls between rooms). |
| adjacencyProbeDistancesMm | number[] | no | [250,500,750] | Probe distances (mm) used by `excludeWallsBetweenRooms` (multiple distances reduce false negatives for narrow rooms). |
| includeBoundaryColumns | bool | no | true | If true, also processes boundary segments whose element is a Column/StructuralColumn. |
| restrictBoundaryColumnsToEligibleWalls | bool | no | false | If true, only processes boundary column segments when the column is adjacent to an **eligible** wall segment (after filters like `onlyRcCore` / `excludeWallsBetweenRooms`). |
| columnIds | int[] | no |  | Optional explicit column elementIds to consider (only used when `tempEnableRoomBoundingOnColumns=true`). |
| autoDetectColumnsInRoom | bool | no | true | If true, auto-detects candidate columns near the room bbox (best-effort). |
| searchMarginMm | number | no | 1000.0 | Bbox expansion when auto-detecting columns. |
| tempEnableRoomBoundingOnColumns | bool | no | true | Temporarily sets column `Room Bounding=ON` to force column segments into `Room.GetBoundarySegments`, then restores. |
| skipExisting | bool | no | true | If true, tries to detect an existing finish wall of the same type on the room side (per segment). |
| existingMaxOffsetMm | number | no | 120.0 | Max distance between the **computed creation baseline** and an existing finish wall baseline (2D). |
| existingMinOverlapMm | number | no | 100.0 | Required overlap length along the segment direction (2D). |
| existingMaxAngleDeg | number | no | 3.0 | Parallel tolerance (deg). |
| existingSearchMarginMm | number | no | 2000.0 | Candidate search expansion around the room boundary bbox (XY). |
| heightMm | number | no |  | Optional override for the created wall unconnected height. |
| baseOffsetMm | number | no |  | Optional override for the created wall base offset. |
| roomBounding | bool | no | false | Default behavior is to set created walls `Room Bounding=OFF`. Set `true` to keep as-is. |
| probeDistMm | number | no | 200.0 | Probe distance used to pick the “inside” offset direction (best-effort). |
| joinEnds | bool | no | true | If true, sets wall end joins to “Allow Join” (best-effort). |
| joinType | string | no | miter | `miter`/`butt`/`squareoff` (best-effort, only when a join exists). |
| cornerTrim | bool | no | true | If true, corner-trims the computed baselines so consecutive segments connect (miter-like). |
| cornerTrimMaxExtensionMm | number | no | 2000.0 | Max allowed endpoint extension when corner-trimming (0 = unlimited). |

## Example (JSON-RPC)
Apply `"(内壁)W5"` to the selected room boundary, RC core walls only:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "room.apply_finish_wall_type_on_room_boundary",
  "params": {
    "fromSelection": true,
    "newWallTypeNameOrId": "(内壁)W5",
    "onlyRcCore": true,
    "coreMaterialNameContains": "*コンクリート",
    "boundaryLocation": "Finish",
    "skipExisting": true,
    "joinEnds": true
  }
}
```

## Result (shape)
`ok=true` returns `result` with:
- `createdWallIds` (`int[]`), `createdCount`
- `segments[]`: per boundary segment results (`loopIndex`, `segmentIndex`, `boundaryElementId`, `boundaryElementKind`, `hostWallId`/`hostColumnId`, `status`, optional `createdWallId`)
- `skipped[]`: non-wall segments / non-matching segments, etc.

## Notes
- Difference vs `element.create_flush_walls`: this command uses the room boundary **segment length** (default `Finish`). `element.create_flush_walls` uses the **full source wall `LocationCurve`** length.
- The offset baseline is computed from the boundary curve and the new wall type thickness, then the new wall is created as an unconnected-height wall on the room level.
- `skipExisting=true` compares against the same computed baseline that would be used for creation (not the raw boundary curve). This avoids “same-place duplicates” when re-running with the same inputs.
- If `cornerTrim=true` (default), baselines are corner-trimmed so consecutive segments connect and joins can form continuous corners (best-effort; most reliable for `Line` segments).
- If `excludeWallsBetweenRooms=true`, the “other room across the wall” check uses the same probing strategy as `get_candidate_exterior_walls`:
  - samples multiple points along the segment,
  - tests both sides (`± wall.Orientation`) at `adjacencyProbeDistancesMm`,
  - considers “room exists” by `Room.IsPointInRoom` using each Room’s base level Z (XY-focused).
- For columns:
  - With defaults (`includeBoundaryColumns=true`, `autoDetectColumnsInRoom=true`, `tempEnableRoomBoundingOnColumns=true`), the command temporarily enables column `Room Bounding` so column segments appear in `Room.GetBoundarySegments`, creates finish walls, then restores the original value.
  - The response includes `autoDetectedColumnIds` and `toggledColumnIds` for debugging.
  - If columns are still missing, increase `searchMarginMm` or pass explicit `columnIds`.
