# Apply Finish Wall Type on Room Boundary (segment-length)

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
| includeBoundaryColumns | bool | no | true | If true, also processes boundary segments whose element is a Column/StructuralColumn. |
| skipExisting | bool | no | true | If true, tries to detect an existing finish wall of the same type on the room side (per segment). |
| existingMaxOffsetMm | number | no | 120.0 | Max distance between the boundary segment and an existing finish wall baseline (2D). |
| existingMinOverlapMm | number | no | 100.0 | Required overlap length along the segment direction (2D). |
| existingMaxAngleDeg | number | no | 3.0 | Parallel tolerance (deg). |
| existingSearchMarginMm | number | no | 2000.0 | Candidate search expansion around the room boundary bbox (XY). |
| heightMm | number | no |  | Optional override for the created wall unconnected height. |
| baseOffsetMm | number | no |  | Optional override for the created wall base offset. |
| roomBounding | bool | no | false | Default behavior is to set created walls `Room Bounding=OFF`. Set `true` to keep as-is. |
| probeDistMm | number | no | 200.0 | Probe distance used to pick the “inside” offset direction (best-effort). |
| joinEnds | bool | no | true | If true, sets wall end joins to “Allow Join” (best-effort). |
| joinType | string | no | miter | `miter`/`butt`/`squareoff` (best-effort, only when a join exists). |

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
- Column segments are only detected when the column is room-bounding and appears in `Room.GetBoundarySegments`. If columns are missing, check the element’s `Room Bounding` setting or the room boundary configuration.
