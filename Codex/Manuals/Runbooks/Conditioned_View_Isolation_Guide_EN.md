# Conditioned View Isolation Guide (High-Performance, Locale-Agnostic)

Purpose
- Create a Seed view (no target category) and per-condition views (one per type/value) from the current view, with high reliability and minimal UI stalls.
- Works across templates/locales; avoids fragile name matching; time-sliced for large models.

Prerequisites
- Revit with MCP add-in running (default port 5210).
- PowerShell 7+/Python 3.x available.
- Durable sender: `Scripts/Reference/send_revit_command_durable.py`.

Key Ideas
- Detach view template on duplicates before any writes.
- Prefer TypeId/parameter-Id rules over display strings (locale-agnostic).
- For isolation, explicitly hide the non-target set (category/class/parameter mismatch) instead of trying to “unhide everything”.
- Use batching (`batchSize`, `maxMillisPerTx`) and time slicing with `nextIndex` for large views.

Workflow
1) Select Base View
   - Use the current graphical view, or choose one with the most relevant elements visible (e.g., walls: categoryId -2000011).

2) Create Seed View (no target category)
   - Duplicate the base view with detailing: `duplicate_view { withDetailing:true }`.
   - Reset lightly in the duplicate: `show_all_in_view { detachViewTemplate:true, includeTempReset:true, unhideElements:false, clearElementOverrides:false }`.
   - Hide the target category elements (e.g., all walls) in batches: `hide_elements_in_view { elementIds:[...], batchSize:800 }`.
   - Rename to `Seed`.

3) Define “Condition” and Strategy
   - Category/Class-based: keep annotations as needed via `keepAnnotations`.
   - Parameter-based (recommended): use numeric IDs (e.g., wall TypeId via `ELEM_TYPE_PARAM`) for robust matching.
   - Two robust strategies:
     - Explicit-hide (recommended): Compute non-target ids and pass them to `hide_elements_in_view`.
     - Single-call filter: `isolate_by_filter_in_view` with `parameterRules` and `keepAnnotations:false` when safe.

4) Build Per-Condition Views
   - Group element ids by the condition value in the base view (e.g., walls grouped by `typeId`).
   - For each group:
     - Duplicate the base view, reset lightly (detach+temp hide/isolate off).
     - Apply isolation:
       - Explicit-hide: hide non-walls + walls of other types.
       - Or call `isolate_by_filter_in_view` with `{ includeClasses:["Wall"], parameterRules:[ { target:'instance', builtInName:'ELEM_TYPE_PARAM', op:'eq', value:<TypeId> } ] }`.
     - Rename the view to a friendly name (e.g., the type’s display name).

Performance & Reliability
- Detach templates (`detachViewTemplate:true`) to avoid template locks.
- For heavy views, use `batchSize≈800`, `maxMillisPerTx≈3000ms`. Clients should loop on `nextIndex` until `completed:true`.
- Prefer TypeId over Type Name; avoid locale-dependent string filters.
- Avoid global “unhide all” passes; isolate by hiding the non-target set.

Example (PowerShell; Seed + per-type walls)
```
pwsh -ExecutionPolicy Bypass -File Codex/Scripts/Reference/create_seed_and_type_views_typeid.ps1 -Port 5210 -BatchSize 800 -MaxMillisPerTx 3000
```

Example (Python; one-shot isolate by TypeId)
```
python Codex/Scripts/Reference/send_revit_command_durable.py --port 5210 --command isolate_by_filter_in_view --params '{
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
  }
}'
```

Troubleshooting
- Grids/annotations remain visible: set `keepAnnotations:false` or hide annotation categories explicitly.
- Stalls on reset: use the lightweight `show_all_in_view` (no unhide/clear), then explicit-hide non-targets.
- Long operations: reduce `batchSize` (e.g., 200) and keep `maxMillisPerTx` around 2000–3000ms.




