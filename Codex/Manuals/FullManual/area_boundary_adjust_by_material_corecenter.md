# area_boundary_adjust_by_material_corecenter

- Category: Area
- Purpose: Adjust existing Area Boundary Lines to a specified material layer centerline (core center) based on selected walls.

## Overview
Runs in an **Area Plan** view.
- You select **Areas + Walls** (or pass `area_element_ids` / `wall_element_ids`).
- Boundary lines are collected from the selected Areas’ boundary loops (safe, area-scoped).
- Each selected wall computes a target curve (same layer rule as `area_boundary_create_by_material_corecenter`).
- A boundary line is matched by **nearest + parallel** within the **match tolerance** (`options.tolerance_mm`), then replaced by **delete + recreate**.
- Corner fixing is best-effort **line-line only** (`corner_resolution: trim_extend`) and uses its own tolerance (`options.corner_tolerance_mm`).
- Flipped walls are supported: the offset direction uses the wall’s **exterior direction** (`Wall.Orientation`), validated against the exterior face normal when available.
- If a wall is longer than the matched boundary segment (e.g., one wall spans multiple Rooms), the target curve is **clipped to the matched boundary extents** before replacement to avoid runaway lines that break enclosure.

If you need to allow **non-core** layers to be selected even when the material exists in the core, set `options.include_noncore=true` (legacy selection rule).

If a wall does **not** contain the specified material in its CompoundStructure layers, it is skipped by default. Set `options.fallback_to_core_centerline=true` to instead fall back to the wall **core centerline** (or **wall centerline** when no core exists).

## Usage
- Method: area_boundary_adjust_by_material_corecenter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | yes* |  |
| materialName | string | yes* |  |
| material | object | yes* |  |
| area_element_ids | int[] | yes** |  |
| wall_element_ids | int[] | no/depends | selection |
| viewId | int | no | active view |
| dryRun | bool | no | false |
| refreshView | bool | no | false |
| options.tolerance_mm | number | no | 5.0 |
| options.corner_tolerance_mm | number | no | auto (5.0 when tolerance_mm &gt; 10, else tolerance_mm) |
| options.match_strategy | string | no | nearest_parallel |
| options.corner_resolution | string | no | trim_extend |
| options.parallel_threshold | number | no | 0.98 |
| options.allow_view_wide | bool | no | false |
| options.include_debug | bool | no | true |
| options.include_layer_details | bool | no | false |
| options.include_noncore | bool | no | false |
| options.fallback_to_core_centerline | bool | no | false |

*Provide one of: `materialId`, `materialName`, or `material:{id|name}`.  
**For safety, `area_element_ids` is required unless `options.allow_view_wide=true`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "area_boundary_adjust_by_material_corecenter",
  "params": {
    "material": { "name": "Concrete" },
    "area_element_ids": [777, 778],
    "wall_element_ids": [111, 222],
    "viewId": 444,
    "options": {
      "tolerance_mm": 5.0,
      "match_strategy": "nearest_parallel",
      "corner_resolution": "trim_extend",
      "parallel_threshold": 0.98,
      "include_debug": true
    }
  }
}
```

## Result (high level)
- `adjustedBoundaryLineIdMap`: `{oldId,newId,wallId,...}` list
- `wallResults`: per-wall match diagnostics
- `warnings`: skip reasons and corner cases
- `toleranceMm`: match tolerance (backward-compat)  
- `matchToleranceMm` / `cornerToleranceMm`: tolerances actually used for matching / corner fixing
