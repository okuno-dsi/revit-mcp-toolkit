# AI Quick Reference: High‑Performance Recipes

This playbook collects fast, reliable ways to accomplish common tasks (parameters, type changes, transforms, baselines + geometry, joins) with performant command patterns, paging, and robust fallbacks.

## Parameters

- Bulk (types) — SI via UnitHelper
  - Command: `get_type_parameters_bulk`
  - Use when: need many type params across categories
  - Input (example):
    ```json
    {
      "categories": [-2001320],
      "paramKeys": [{"name":"H"},{"name":"B"},{"name":"tw"},{"name":"tf"}],
      "page": {"startIndex":0, "batchSize":300}
    }
    ```
  - Notes: Prefer `builtInId`/`guid` over `name`; result includes `params` (SI) and `display`.

- Bulk (instances) — SI via UnitHelper
  - Command: `get_instance_parameters_bulk`
  - Use when: need params across many elements (any category)
  - Input:
    ```json
    { "elementIds": [101,102], "paramKeys": [{"name":"Comments"}], "page": {"startIndex":0,"batchSize":200} }
    ```

- Single/inspecting
  - Commands: `get_param_values`, `get_param_meta`
  - Tip: `get_param_values { mode:"element"|"type", scope:"auto" }` for quick targeted reads.

- High‑perf patterns
  - Gather typeIds first with `get_types_in_view`, then call type bulk.
  - Page large sets (200–500) and stream to CSV incrementally.

## Type Management

- Change type (FamilyInstance)
  - Command: `change_family_instance_type`
  - Input (by name): `{ "elementId": 12345, "typeName": "HV2", "categoryId": -2001320 }`
  - Input (by id): `{ "elementId": 12345, "typeId": 67890 }`

- Change type (Structural Frame)
  - Command: `change_structural_frame_type`
  - Same input shape (`elementId` + `typeId`|`typeName`).

- Duplicate type
  - Commands: `duplicate_*_type` variants (e.g., `duplicate_structural_frame_type`)
  - Input: `{ "typeId": 123, "newName": "NewType" }`

## Transforms (move/rotate/reshape)

- FamilyInstance move
  - Command: `move_family_instance`
  - Input (mm): `{ "elementId": 12345, "offset": {"x":100, "y":0, "z":0} }`

- Structural Frame move (by delta)
  - Command: `move_structural_frame`
  - Input (mm): `{ "elementId": 12345, "offset": {"x":0, "y":0, "z":100} }`

- Structural Frame reshape (set endpoints)
  - Command: `update_structural_frame_geometry`
  - Input (mm): `{ "elementId": 12345, "start": {x,y,z}, "end": {x,y,z} }`

- General delta (any element)
  - Command: `apply_transform_delta`
  - Use when: need a raw transform delta; limit to small batches.

## Baselines + Geometry

- Frame endpoints
  - Command: `get_structural_frames { elementId }`
  - Returns: `start/end` in mm; ideal for alignment, gap checks, or `analyze_segments` inputs.

- Wall baseline (LocationCurve)
  - Command: `get_wall_baseline { elementId }`
  - Returns: `{kind:"line", start/end}` or polyline points (mm).

- Geometry relations (2D/3D)
  - Command: `analyze_segments`
  - 3D example (point → baseline distance):
    ```json
    { "mode":"3d", "seg1": {"a":{x,y,z},"b":{x,y,z}}, "seg2": {"a":{x,y,z},"b":{x,y,z}}, "point": {x,y,z}, "tol": {"distMm":0.1} }
    ```

- Batch mesh with analytic fallbacks
  - Command: `get_instances_geometry`
  - Input (selection): `{ "fromSelection":true, "detailLevel":"Fine", "includeAnalytic":true, "page": {"startIndex":0, "batchSize":200} }`
  - Output per element:
    - Mesh: `vertices/normals/submeshes/materials` (feet)
    - Analytic (mm): Rooms/Spaces→`contours`; LocationCurve→`wire`; LocationPoint→`point`

- Single mesh
  - Command: `get_instance_geometry { elementId }`
  - Use for one‑offs or debugging mesh content.

- Bounding box
  - Command: `get_bounding_box { elementId }`

## Join Geometry

- Join two elements
  - `join_elements { "elementIdA": id1, "elementIdB": id2 }`

- Unjoin
  - `unjoin_elements { "elementIdA": id1, "elementIdB": id2 }`

- Joined?
  - `are_elements_joined { "elementIdA": id1, "elementIdB": id2 }`

- Switch join order
  - `switch_join_order { "elementIdA": id1, "elementIdB": id2 }`

- List joined partners
  - `get_joined_elements { "elementId": id }`

## View Scanning & Selection

- Current view
  - `get_current_view` → `viewId`

- Fast type discovery in view
  - `get_types_in_view { viewId, categories? }` → `types[] {typeId,count}`

- Elements in view (IDs only)
  - `get_elements_in_view { viewId, _shape:{ idsOnly:true }, _filter:{ modelOnly:true, excludeImports:true } }`

- Get details in chunks
  - For `elementIds` in 200‑item pages → `get_element_info { rich:true }`

- Selection helpers
  - `get_selected_element_ids` / `select_elements`

## Performance Tips

- Prefer bulk endpoints for parameters (type/instance) and page over large sets.
- Use `get_types_in_view` → `get_type_parameters_bulk` for type‑first flows.
- For geometry, use `get_instances_geometry` with `detailLevel` + `weldToleranceMm` and `page`.
- Filter early by category and view; request IDs first, then detail.
- Prefer `builtInId`/`guid` param keys to avoid locale issues; use `display` for human labels.
- Respect units: params (SI mm/deg), meshes (feet), analytic output (mm).

## Quick CLI Pattern

```bash
# Durable sender (Python) – replace method/params as needed
python -X utf8 Manuals/Scripts/send_revit_command_durable.py \
  --port 5210 --command <method> --params '<json>' --force \
  --wait-seconds 180 --timeout-sec 600
```

Links
- Parameters (Bulk): `Manuals/Commands/Bulk_Parameters_EN.md`
- Batch Mesh: `Manuals/Commands/Get_Instances_Geometry_EN.md`
- Geometry Analyze: `Manuals/Commands/Analyze_Segments_EN.md`
- Join Geometry: `Manuals/Commands/Join_Geometry_EN.md`
- Type Actions: per‑category commands + `change_family_instance_type`, `change_structural_frame_type`
