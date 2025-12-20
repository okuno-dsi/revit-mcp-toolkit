# Shape and Paging Options (Reads)

Many read commands support lightweight shaping to reduce payload size and improve responsiveness.

## Common Options

- `_shape.idsOnly:boolean`
  - Returns only `elementIds` (or `typeIds`) for quick enumeration.
- `_shape.page:{ limit?:number, skip?:number }`
  - Applies paging. `limit=0` (or omitted with `summaryOnly:true`) can be used to perform a count-only query.
- `summaryOnly:boolean`
  - Returns only metadata like `totalCount` without items.
- Command-specific flags
  - `includeLocation:boolean`, `includeBaseline:boolean`, `includeEndpoints:boolean`, etc. to opt-in/out heavier calculations.

## Examples

Doors (IDs only, 200 per page)
```
{ method:"get_doors", params:{ _shape:{ idsOnly:true, page:{ limit:200, skip:0 } } } }
```

CurtainWalls (names only, first 100)
```
{ method:"get_curtain_walls", params:{ namesOnly:true, _shape:{ page:{ limit:100 } } } }
```

Floors (summary only)
```
{ method:"get_floors", params:{ summaryOnly:true } }
```

Stairs (without location for lighter output)
```
{ method:"get_stairs", params:{ includeLocation:false, _shape:{ page:{ limit:100 } } } }
```

## Supported Commands (current)
- Doors: `get_doors`, `get_door_types`
- Floors: `get_floors`, `get_floor_types`
- CurtainWalls: `get_curtain_walls`, `get_curtain_wall_types`
- Roofs: `get_roofs`
- Materials: `get_materials` (list supports `idsOnly`/paging; parameters support paging/summaryOnly)
- Windows: `get_window_parameters`, `get_window_type_parameters` (supports `_shape.page.limit/skip`, `summaryOnly`; keep `namesOnly` for light usage)
- Doors: `get_door_parameters`, `get_door_type_parameters` (supports `_shape.page.limit/skip`, `summaryOnly`; keep `namesOnly` for light usage)
- Stairs: `get_stairs`, `get_stair_types`, `get_stair_parameters`, `get_stair_type_parameters`
- Railing: `get_railings`, `get_railing_types`
- Ceilings: `get_ceilings`
- Mass: `get_mass_instances`
- In‑Place Families: `get_inplace_families`
- FamilyInstances: `get_family_instances` (now supports `idsOnly`/paging/summaryOnly)
- Architectural Columns: `get_architectural_columns`, `get_architectural_column_types`
- Face: `get_face_regions` (uses `_shape.page` mapped to `regionOffset/regionLimit`)
  - `get_face_region_detail` supports `summaryOnly` to return light metadata without geometry/mesh/spatial probing.

Notes
- When both legacy `skip/count` and `_shape.page` are provided, `_shape.page` takes precedence in most updated commands.
- Command results may change field names slightly (e.g., `instances`, `masses`, `ceilings`) while preserving legacy shapes; prefer `idsOnly` for bulk scans.
