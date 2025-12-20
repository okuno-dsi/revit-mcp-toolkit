Commands Index (English, AI‑oriented)

Purpose
- Single place to browse all MCP command names, grouped by category with importance tags, and a machine‑readable index for tooling.

Files
- Commands_Index.all.en.md — Human‑friendly, all commands by category with (high/normal/low) and read/write hint.
- commands_index.json — Machine‑readable map: { method: { category, importance, kind } }.
- generate_commands_index.py — Generator script (reads names from the demo data folder).
 - Rename_Types_By_Parameter_EN.md — Bulk rename types by parameter value (prefix/suffix rules, tokens, paging).
 - Rename_Types_Bulk_EN.md — Explicit mapping (typeId/uniqueId → newName) high‑throughput bulk rename.

Update Steps (when commands are added/removed)
1. Refresh the live command names (namesOnly):
   - `python Manuals/Scripts/send_revit_command_durable.py --port <PORT> --command list_commands --params '{"namesOnly":true}' --output-file list_commands_names.json`
   - Alternatively place an array of names in available_commands.json`.
2. Generate the index:
   - `python Commands_Index/generate_commands_index.py`
3. Review the diffs (importance/category heuristics) and commit.

Notes
- Importance and category are heuristic; adjust the `HIGH` set and keyword buckets in the generator as needed.
- For exact parameters/behavior, prefer the live environment and per‑command docs.

Global parameter-update inputs (robustness)
- All write commands that update element/element-type parameters now accept any of the following keys to identify a parameter (priority order):
  - `builtInId` (int, BuiltInParameter numeric), then `builtInName` (string, enum name, e.g. `ALL_MODEL_TYPE_MARK`), then `guid` (string), then `paramName` (localized display name).
- This applies to: `update_*_parameter`, `set_*_parameter`, and `*_type_parameter` commands across Walls/Doors/Windows/CurtainWalls/Levels/Views/Materials/MEP/Railings/Stairs/Structural*/Floors/Roofs/RevisionClouds.
- Backwards compatible: existing `paramName` payloads still work. Prefer `builtInId`/`builtInName`/`guid` for locale‑agnostic workflows.



Deprecations (view visibility)
- The following are now superseded by the snapshot flow and removed from the add-in:
  - reset_all_view_overrides, reset_category_override, set_category_visibility, unhide_elements_in_view
- Prefer:
  - save_view_state (read) to capture template/temporary hide, category/filter/workset visibility (optionally hidden elements)
  - restore_view_state (write) to reapply the captured state
  - show_all_in_view for emergency 'reveal everything', coupled with restore_view_state to return to the prior state

View duplication (idempotent-friendly)
- duplicate_view now accepts:
  - desiredName: strict target name to assign to the duplicated view
  - onNameConflict: 'returnExisting' | 'increment' | 'timestamp' | 'fail' (default: 'increment')
  - idempotencyKey: return the previously created view for the same key (process-local)
- Recommended pattern to avoid accidental multiple copies:
  - `duplicate_view { viewId, desiredName:"<Base> Copy", onNameConflict:"returnExisting", idempotencyKey:"dup:<baseUid>:<Base> Copy" }`
- Note: For renaming existing views, use rename_view { viewId, newName } (do not set the Name parameter).

## Updates (Resilient View/Visualization)
- Updated docs and AI‑friendly JSONL for non‑blocking execution:
  - `Manuals/UPDATED_VIEW_VISUALIZATION_COMMANDS_EN.md`
  - `Manuals/updated_view_visualization_commands.en.jsonl`
- Covers resilient params for:
  - `set_visual_override`, `clear_visual_override`, `set_category_override`,
    `apply_conditional_coloring`, `apply_quick_color_scheme`,
    `create_spatial_volume_overlay`, `hide_elements_in_view`

## Usage Optimization (Recommendations)
- Parameters (scale and robustness): prefer `get_instance_parameters_bulk`/`get_type_parameters_bulk` with `paramKeys` using `builtInId`/`guid` when available. Fall back to `name` only when IDs are not known.

Command availability & versioning
- Treat `list_commands` as the source of truth for what is currently registered in your running Revit instance. Some guides reference commands introduced after earlier builds.
- After changing or rebuilding the add‑in, restart Revit (or reload the add‑in) so newly implemented handlers are registered (e.g., `rename_types_bulk`, `rename_types_by_parameter`).
- If a command is missing:
  - Check you are connected to the correct port/instance.
  - Verify `RevitMcpWorker` registers the command in your build.
  - Warm‑up sequence can help: `ping_server` → `agent_bootstrap` → `list_commands`.
- Boolean/on-off reads: normalize values defensively across `params` and `display` (true/yes/on/はい/checked; nonzero numerics are ON). See Bulk_Parameters_EN.md for details.
- View rename: use `rename_view { viewId, newName }` (do not rely on `set_view_parameter` for view name). Alternatively, set `namePrefix` during `duplicate_view`.
- Templates and visibility: for operations blocked by templates, use `detachViewTemplate:true` (or snapshot+restore via `save_view_state`/`restore_view_state`).
- Visibility debugging: use `audit_hidden_in_view` to distinguish `category_hidden` vs `hidden_in_view`, then fix with `set_category_visibility` or `unhide_elements_in_view` accordingly.

## New (Type Rename Helpers)
- `rename_types_by_parameter`: Flexible, rule‑based rename for ElementTypes using a parameter value (supports tokens `{value}/{display}/{display_no_space}`, idempotent prefixes, paging, and dry‑run). See Rename_Types_By_Parameter_EN.md.
- `rename_types_bulk`: Explicit list mapping for high‑throughput renames (single slice transaction; conflict policy `skip|appendNumber|fail`; dry‑run). See Rename_Types_Bulk_EN.md.
