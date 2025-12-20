# area_boundary_create_by_material_corecenter

- Category: Area
- Purpose: Create Area Boundary Lines aligned to a specified material layer centerline (core center) along selected walls.

## Overview
Runs in an **Area Plan** view. For each wall, this command:
- Reads the wall type **CompoundStructure**.
- Finds layers matching the specified `materialId`.
- Picks the target layer by rule (default = core-priority):
  1) if any match is within Core range → pick **within core** (Structure → thickest, else thickest)
  2) else `Function == Structure` → thickest
  3) else thickest among matches
- Offsets the wall `LocationCurve` to the **target layer center** and creates **Area Boundary Lines**.
- Flipped walls are supported: the offset direction uses the wall’s **exterior direction** (`Wall.Orientation`), validated against the exterior face normal when available.

If you need to allow **non-core** layers to be selected even when the material exists in the core, set `options.include_noncore=true` (legacy selection rule).

If a wall does **not** contain the specified material in its CompoundStructure layers, it is skipped by default. Set `options.fallback_to_core_centerline=true` to instead fall back to the wall **core centerline** (or **wall centerline** when no core exists).

Curtain/stacked walls or walls without `LocationCurve` / `CompoundStructure` are skipped with warnings.

## Usage
- Method: area_boundary_create_by_material_corecenter

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| materialId | int | yes* |  |
| materialName | string | yes* |  |
| material | object | yes* |  |
| wall_element_ids | int[] | no/depends | selection |
| viewId | int | no | active view |
| dryRun | bool | no | false |
| refreshView | bool | no | false |
| options.tolerance_mm | number | no | 5.0 |
| options.skip_existing | bool | no | true |
| options.include_debug | bool | no | true |
| options.include_layer_details | bool | no | false |
| options.include_noncore | bool | no | false |
| options.fallback_to_core_centerline | bool | no | false |

*Provide one of: `materialId`, `materialName`, or `material:{id|name}`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "area_boundary_create_by_material_corecenter",
  "params": {
    "material": { "name": "Concrete" },
    "wall_element_ids": [111, 222, 333],
    "viewId": 444,
    "dryRun": false,
    "options": {
      "tolerance_mm": 5.0,
      "skip_existing": true,
      "include_debug": true
    },
    "refreshView": false
  }
}
```

## Result (high level)
- `createdBoundaryLineIds`: created area boundary line ids
- `results`: per-wall results (created/skipped + debug)
- `warnings`: skip reasons and corner cases
