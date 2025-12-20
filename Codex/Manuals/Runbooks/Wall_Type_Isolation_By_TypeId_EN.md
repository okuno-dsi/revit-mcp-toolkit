# Robust Wall Type Isolation (TypeId‑based, Annotation‑Agnostic)

Scope
- Show only one wall type (or per‑type views) reliably across different projects and locales. Avoids pitfalls of template locks, name mismatches, and annotation bleed‑through.

Why previous attempts sometimes “show everything”
- keepAnnotations default: `isolate_by_filter_in_view` keeps annotation categories by default. Grids (OST_Grids) are annotations, so they remain visible unless explicitly hidden.
- Name matching fragility: Filtering by `SYMBOL_NAME_PARAM` (type name) can fail for system families or across locales, causing broad matches that leave many wall types visible.

Core idea
- Use TypeId (numeric) instead of Type Name, and explicitly hide:
  - all non‑wall elements, and
  - all walls whose `ELEM_TYPE_PARAM` != target TypeId.
- This is independent of language, template state, and category display quirks.

Prerequisites
- Revit running with Revit MCP add‑in (default port `5210`).
- PowerShell 7+ and Python 3.x available.
- Durable sender: `Codex/Manuals/Scripts/send_revit_command_durable.py`.

Commands used (server)
- View ops: `duplicate_view`, `show_all_in_view`, `activate_view`, `set_view_parameter`
- Query: `get_current_view`, `get_elements_in_view` (with `_filter` + `_shape`), `get_element_info`
- Visibility: `hide_elements_in_view`

1) One‑Type Safe Isolation (Recommended)
- Script: `Codex/Manuals/Scripts/isolate_single_wall_type_safe.ps1`
- What it does:
  1) Duplicate the active view (with detailing), detach template, unhide and clear overrides.
  2) Collect wall element ids and fetch element info to obtain `typeId` per element.
  3) Determine target `TypeId` (auto‑select the most frequent type if not provided).
  4) Hide non‑walls (modelOnly, excludeImports, excludeCategoryIds=[-2000011]) and hide all walls whose TypeId differs from the target.
  5) Optionally activate the new view.

PowerShell
```
# Auto‑select the most frequent wall type in the duplicated view
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/isolate_single_wall_type_safe.ps1 -Port 5210 -Activate

# Or specify an explicit TypeId
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/isolate_single_wall_type_safe.ps1 -Port 5210 -TypeId 403 -Activate
```

Notes
- Batching and timeslicing: the script uses `batchSize` and `maxMillisPerTx` to keep UI responsive.
- Works the same regardless of language; no reliance on Type Name strings.

2) Per‑Type Views (Seed + TypeId‑based Isolation)
- Script: `Codex/Manuals/Scripts/create_seed_and_type_views_typeid.ps1`
- What it does:
  1) Seed view: duplicate active view, detach template, reset, hide all walls → rename to `Seed`.
  2) Group all walls by `typeId` in the active view.
  3) For each `typeId`, duplicate a fresh view, reset, then isolate that type by hiding non‑walls and other wall types.
  4) Rename each view to the human‑friendly type name for readability (isolation is done by numeric TypeId under the hood).

PowerShell
```
# Create/refresh Seed + per‑type views (deletes previous run’s views)
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/create_seed_and_type_views_typeid.ps1 -Port 5210

# Reuse previously created views when present (no delete)
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/create_seed_and_type_views_typeid.ps1 -Port 5210 -ReuseExisting
```

3) If you prefer a single‑call filter
- You can still call `isolate_by_filter_in_view` using TypeId:
```
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command isolate_by_filter_in_view --params '{
  "viewId": <id>,
  "detachViewTemplate": true,
  "reset": true,
  "keepAnnotations": false,
  "filter": {
    "includeClasses": ["Wall"],
    "includeCategoryIds": [-2000011],
    "parameterRules": [
      { "target": "instance", "builtInName": "ELEM_TYPE_PARAM", "op": "eq", "value": <TypeId> }
    ],
    "logic": "all"
  },
  "__smoke_ok": true
}'
```
- Important: set `keepAnnotations:false` to avoid grids (annotation category) sticking around.
- Even so, for maximum reliability across models, prefer the explicit hide approach in sections 1–2.

4) Best Practices
- Template locks: always detach view template (`detachViewTemplate:true`) and perform a best‑effort reset (`show_all_in_view` with unhide + clear overrides) on duplicates.
- Batching: use `batchSize` ≈ 800 and `maxMillisPerTx` ≈ 3000ms for large views.
- IDs: never send `viewId: 0` or `elementId: 0`.
- Re‑runs: use `-ReuseExisting` to keep prior views; otherwise the runbook deletes views from the previous run safely.

5) Troubleshooting
- Grids (centerlines) still visible:
  - When using `isolate_by_filter_in_view`, set `keepAnnotations:false`.
  - With explicit hide, ensure the non‑wall set uses `_filter` with `excludeCategoryIds=[-2000011]` and `modelOnly=true`.
- Too many walls kept:
  - Do not filter by `SYMBOL_NAME_PARAM`. Use `ELEM_TYPE_PARAM == <TypeId>` or the explicit hide of “other walls” by `typeId`.
- Long operations / UI stalls:
  - Reduce `batchSize` (e.g., 200) and keep `maxMillisPerTx` ≈ 2000–3000ms.

Appendix: Commands reference
- `duplicate_view`, `show_all_in_view`, `activate_view`, `set_view_parameter`
- `get_current_view`, `get_elements_in_view` (`_filter` + `_shape`), `get_element_info`
- `hide_elements_in_view`

This manual codifies a reliable, locale‑agnostic way to isolate wall types using TypeId. Prefer the explicit hide flow for consistency; use the one‑shot filter only when you can safely set `keepAnnotations:false` and confirm rule matching.

