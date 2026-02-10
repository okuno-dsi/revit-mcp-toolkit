# get_room_finish_takeoff_context

- Category: Room
- Purpose: Collect a single “finish takeoff context” bundle for a room (boundary segments + matched walls + columns + hosted door/window inserts).

## Overview
This command is designed for finish takeoff workflows where the agent/tool needs:
- Room boundary segments (with segment length and boundary element linkage),
- A robust “nearby walls” match for each boundary segment (2D geometry, configurable tolerances),
- Optional column collection near/in the room, and
- Optional inserts (Doors/Windows) hosted by the matched walls (grouped per wall).

It can also **temporarily enable Room Bounding on columns** (for boundary calculation) inside a `TransactionGroup` that is always rolled back, so your model is not modified.

## Usage
- Method: `get_room_finish_takeoff_context`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| roomId | int | no |  | If omitted, use selection (`fromSelection=true`). |
| fromSelection | bool | no | auto | If true, tries to resolve a selected `Room` (or `RoomTag`) as target. |
| includeIslands | bool | no | true | Include inner loops (islands). |
| includeSegments | bool | no | true | Output `loops[].segments[]` with coordinates and lengths. |
| boundaryLocation | string | no | Finish | `Finish`/`Center`/`CoreCenter`/`CoreBoundary` (see `SpatialUtils.ParseBoundaryLocation`). |
| includeWallMatches | bool | no | true | Enables wall matching near boundary segments. |
| wallMaxOffsetMm | number | no | 300.0 | Max distance between boundary segment and wall baseline. |
| wallMinOverlapMm | number | no | 100.0 | Required overlap along segment direction. |
| wallMaxAngleDeg | number | no | 3.0 | Parallel tolerance (deg). |
| wallSearchMarginMm | number | no | 5000.0 | Candidate wall search expansion around room boundary bbox (XY). |
| includeInserts | bool | no | true | Collect door/window inserts hosted by matched walls. |
| includeWallTypeLayers | bool | no | true | Returns wall type layer/material info (reference only). |
| columnIds | int[] | no | [] | Columns to consider (for temp room-bounding + output). |
| autoDetectColumnsInRoom | bool | no | false | Auto-detect columns in/near the room (bbox + `IsPointInRoom`). |
| searchMarginMm | number | no | 1000.0 | Search margin for auto-detect columns. |
| tempEnableRoomBoundingOnColumns | bool | no | true | If true, temporarily sets `Room Bounding=ON` for the columns (rolled back). |
| columnWallTouchMarginMm | number | no | 50.0 | Column↔wall “touch” is approximated by bbox intersection with this margin. |
| includeFloorCeilingInfo | bool | no | true | Collect “related” floors/ceilings (bbox + room interior sampling) for reporting. |
| floorCeilingSearchMarginMm | number | no | 1000.0 | Candidate search expansion around the room boundary bbox (XY). |
| segmentInteriorInsetMm | number | no | 100.0 | Inset distance (towards room interior) used when sampling points near each boundary segment. |
| floorCeilingSameLevelOnly | bool | no | true | If true, floor/ceiling candidates are filtered to the room's `LevelId` (avoids mixing other levels). |
| includeInteriorElements | bool | no | false | Collect “takeoff object” elements inside the room (model categories only; excludes lines/grids by default). |
| interiorCategories | string[] | no | ["Walls","Floors","Ceilings","Roofs","Columns","Structural Columns","Structural Framing","Doors","Windows","Furniture","Furniture Systems","Casework","Specialty Equipment","Generic Models","Mechanical Equipment","Plumbing Fixtures","Lighting Fixtures","Electrical Equipment","Electrical Fixtures"] | Categories to check (friendly names like "Furniture" or BuiltInCategory names like "OST_Furniture"). |
| interiorElementSearchMarginMm | number | no | 1000.0 | Candidate search expansion around the room boundary bbox (XY). |
| interiorElementSampleStepMm | number | no | 200.0 | Sampling step used to estimate the curve length inside the room. |
| interiorElementPointBboxProbe | bool | no | true | For point-based elements (LocationPoint / BBox center), also probe the element bbox footprint (corners+midpoints at `zProbe`) when the reference point is outside the room. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_finish_takeoff_context",
  "params": {
    "fromSelection": true,
    "boundaryLocation": "Finish",
    "includeWallMatches": true,
    "includeInserts": true,
    "includeWallTypeLayers": true,
    "autoDetectColumnsInRoom": true,
    "searchMarginMm": 1000.0
  }
}
```

## Response Notes (key fields)
- `loops[].segments[]` includes:
  - `lengthMm`, `curveType`
  - `boundaryKind`: `Wall` / `Column` / `Other`
  - `boundaryElementId`, `boundaryCategoryName`, `boundaryElementClass`
- `metrics.estWallAreaToCeilingM2` is an **estimate** based on:
  - `wallPerimeterMm` × `roomHeightMm`
  - Openings (doors/windows) are **not subtracted** in this version.
- Perimeter comparison helpers:
  - `metrics.perimeterParamMm` / `metrics.perimeterParamDisplay`: Revit’s Room Perimeter parameter.
  - `metrics.perimeterMm`: perimeter measured from returned boundary segments.
- `walls[]` contains wall matches with `segments[]` pairs.
- `wallIdsBySegmentKey` maps `"loopIndex:segmentIndex"` → `[wallId,…]` for quick lookup.
- `insertsByWall[]` + `inserts.doors/windows[]` organizes openings per wall.
- `wallTypeLayers[]` is reference-only: compound structure layers and materials for each wall type used by matched walls.
- Floor/Ceiling context (reporting aids):
  - `room.levelElevationMm`, `room.baseOffsetMm` / `room.baseOffsetDisplay`
  - `floors[]`: candidate floors intersecting the sampled interior points (default: same-level only; height is `topHeightFromRoomLevelMm`, plus `levelId/levelName`)
  - `ceilings[]`: candidate ceilings intersecting the sampled interior points (default: same-level only; height is `heightFromRoomLevelMm`, plus `levelId/levelName`)
  - `ceilingIdsBySegmentKey`: `"loopIndex:segmentIndex"` → `[ceilingId,…]` (approximate)
  - `loops[].segments[].ceilingHeightsFromRoomLevelMm`: per-segment ceiling height samples (when `includeSegments=true`)
- Interior elements (room-contained; optional):
  - `interiorElements.items[]`: elements that are inside/intersect the room (curve elements include `insideLengthMm` estimate).
  - For point-based elements, `measure.referencePointInside=false` with `measure.bboxProbe.used=true` means the element was included because its bbox footprint intersects the room even though its reference point is outside.

## Excel helper
- If you saved a comparison JSON like `test_room_finish_takeoff_context_compare_*.json`, you can (re)generate a standardized `SegmentWallTypes` sheet via:
  - `Scripts/Reference/room_finish_compare_to_excel.py`

## Related
- `get_room_perimeter_with_columns_and_walls`
- `get_room_openings`
- `get_wall_layers`



