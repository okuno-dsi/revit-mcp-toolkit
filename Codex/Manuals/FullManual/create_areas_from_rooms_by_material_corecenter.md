# create_areas_from_rooms_by_material_corecenter

- Category: Area
- Purpose: In one call, copy selected Rooms' boundaries to Area Boundary Lines, create Areas, then adjust those boundaries to a specified material layer centerline (core center).

## Overview
This command is designed for **performance** and **workflow simplicity**. Instead of calling multiple commands (`copy_area_boundaries_from_rooms` → `get_room_centroid` → `create_area` → `area_boundary_adjust_by_material_corecenter` → `get_area_metrics`), it runs the common workflow in a single MCP request.

It must run in an **Area Plan** view. Only Rooms on the **same level** as the Area Plan are processed.

Key behaviors:
- By default, Area boundary lines are created from `Room.GetBoundarySegments()` (calculated boundary).
- If `options.boundaryCurveSource=PreferLineElements`, and a `BoundarySegment.ElementId` points to a `CurveElement` (e.g. a *Room Separation Line*), the command prefers the **line element geometry**. To keep the boundary loop closed, endpoints are clipped to the `BoundarySegment` endpoints (i.e., the line element is used as the reference, but the segment’s start/end are respected).
- Areas are placed using the Room **LocationPoint** (fast). If missing, it falls back to a **solid centroid** (slower).
- Boundary adjustment uses the existing `area_boundary_adjust_by_material_corecenter` implementation.
- `refreshView` is applied **only once** at the end (optional).
- For robustness, the adjust step defaults to `options.fallback_to_core_centerline=true` unless you explicitly pass it.
- Room boundary extraction can be controlled with `options.boundaryLocation` / `boundaryLocation` (default: `CoreCenter`).

## Pre-run checklist (user confirmations)
- If the prompt says “boundary lines”, confirm whether they mean A) computed boundaries (`Room.GetBoundarySegments`) or B) line elements (Room/Area Separation Lines). See `Manuals/FullManual/spatial_boundary_location.md`.
- Confirm `boundaryLocation` (`Finish`/`Center`/`CoreCenter`/`CoreBoundary`) based on your intent (finish face vs wall/core centerline).
- If you need Areas to match the Room area closely, start with `options.adjust_boundaries=false` (pure computed boundary).
- If you need to align Area boundaries to a specific material layer centerline, set `options.adjust_boundaries=true` and provide `materialId/materialName` (can be slower).
- In views with many existing Area Boundary Lines, prefer using a dedicated clean Area Plan / Area Scheme, or clean up nearby boundary networks first.

Notes (common causes when results look wrong):
- Selection: you can select either **Room** elements or **RoomTag** elements; RoomTags are resolved to their host Rooms.
- If your selection disappears quickly (e.g., switching views), the command can fall back to the **last non-empty selection** (very recent only). See `options.selection_stash_max_age_ms`.
- If the Area Plan already contains many Area Boundary Lines, `options.skip_existing=true` will reuse matching segments (safer than creating overlaps). However, an existing boundary network can still affect which region the new Area lands in. If you get `areaM2=0` (Not enclosed) or a region that spreads unexpectedly, run in a clean Area Plan (duplicate the view / use a dedicated Area Scheme) or remove interfering boundary lines near the target Rooms.
- Room boundaries can be noisy (short segments, arcs, slight misalignment). This command follows `Room.GetBoundarySegments()` and uses `mergeToleranceMm` for duplicate checks; complex shapes may still require minor manual trimming/extension in Revit.
- When `options.adjust_boundaries=true`, boundary lines may be recreated/replaced, so IDs can change. See `adjustResult.adjustedBoundaryLineIdMap`.
- The adjust step can be expensive; use `startIndex/batchSize` to split work if needed.

## Usage
- Method: create_areas_from_rooms_by_material_corecenter

### Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| materialId / materialName / material | (int / string / object) | depends* |  |
| viewId / viewUniqueId | (int / string) | no | active view |
| room_element_ids | int[] | no | selection (Rooms on same level only) |
| wall_element_ids | int[] | no | selection (walls only) / auto-collect** |
| refreshView | bool | no | false |
| startIndex | int | no | 0 |
| batchSize | int | no | very large (process all) |
| options.copy_boundaries | bool | no | true |
| options.create_areas | bool | no | true |
| options.adjust_boundaries | bool | no | true |
| options.collect_walls_from_room_boundaries | bool | no | true |
| options.skip_existing | bool | no | true |
| options.mergeToleranceMm | number | no | 3.0 |
| options.return_area_metrics | bool | no | true |
| options.regenerate_before_metrics | bool | no | true |
| options.include_debug | bool | no | false |
| options.selection_stash_max_age_ms | int | no | 5000 |
| options.boundaryLocation | string | no | `CoreCenter` |
| options.boundaryCurveSource | string | no | `BoundarySegment` |
| options.preferLineElements | bool | no | (overrides `boundaryCurveSource` if set) |
| options.compare_room_and_area | bool | no | true |
| boundaryLocation | string | no | (same as `options.boundaryLocation`) |

*When `options.adjust_boundaries=true`, provide one of: `materialId`, `materialName`, or `material:{id|name}`. If `adjust_boundaries=false`, you can omit it.

**If `wall_element_ids` is not provided and no walls are selected, walls can be collected from the Room boundary segments (`options.collect_walls_from_room_boundaries=true`).

### Pass-through options (to `area_boundary_adjust_by_material_corecenter`)
You can pass these under `options` and they will be forwarded to the adjust step:
- `tolerance_mm` / `toleranceMm`
- `corner_tolerance_mm` / `cornerToleranceMm`
- `match_strategy` / `matchStrategy`
- `corner_resolution` / `cornerResolution`
- `parallel_threshold` / `parallelThreshold`
- `include_noncore` / `includeNonCore`
- `include_layer_details` / `includeLayerDetails`
- `include_debug` / `includeDebug`
- `fallback_to_core_centerline` / `fallbackToCoreCenterline`

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_areas_from_rooms_by_material_corecenter",
  "params": {
    "viewId": 11120260,
    "material": { "name": "*コンクリート" },
    "room_element_ids": [11121453, 11121539, 11121675],
    "refreshView": true,
    "options": {
      "mergeToleranceMm": 3.0,
      "tolerance_mm": 5.0,
      "corner_resolution": "trim_extend",
      "match_strategy": "nearest_parallel",
      "collect_walls_from_room_boundaries": true,
      "return_area_metrics": true
    }
  }
}
```

#### Pure computed boundary (match Room area first)
Use `options.adjust_boundaries=false` and you can omit `material`:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_areas_from_rooms_by_material_corecenter",
  "params": {
    "options": {
      "adjust_boundaries": false,
      "boundaryCurveSource": "BoundarySegment",
      "boundaryLocation": "CoreCenter"
    }
  }
}
```

## Result (high level)
- `createdAreaIds`: created Area ids
- `createdBoundaryLineIds`: created Area Boundary Line ids
- `roomResults`: per-room summary (created area id + boundary counts + room/area area comparison)
- `areaRoomComparisonSummary`: quick summary of room↔area area deltas (avg/max)
- `areaMetrics`: (optional) area m² + perimeter mm per created area
- `adjustResult`: raw result from `area_boundary_adjust_by_material_corecenter`
- `timingsMs`: step timings (copy/create/adjust/metrics/total)
- `warnings`: skips and non-fatal issues
