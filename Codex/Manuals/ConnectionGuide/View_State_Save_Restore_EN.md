# View State Save/Restore (EN)

This guide replaces ad-hoc visibility resets with a reliable snapshot/restore flow.

- New commands
  - `save_view_state` (read): capture template binding, temporary hide/isolate, category visibility, filter visibility, workset visibility; optionally element-level hidden IDs.
  - `restore_view_state` (write): reapply a captured state; control which parts to apply with `apply` flags.
  - `show_all_in_view` (write): emergency "reveal everything"; use before/after restore to return.

- Deprecated/removed
  - `reset_all_view_overrides`, `reset_category_override`, `set_category_visibility`, `unhide_elements_in_view`.

## Usage

1) Save
```
python Manuals/Scripts/send_revit_command_durable.py --port <PORT> --command save_view_state --params "{}" --output-file Work/<ProjectName>_<Port>/Logs/view_state.json
```
- To include element-level hidden IDs: `--params "{\"includeHiddenElements\":true}"`

2) Work
- Perform your operations (e.g., `show_all_in_view`, edits, exports).

3) Restore
```
# Prepare payload with the captured `state`
{ "viewId": <viewId>, "state": { ... }, "apply": { "template": true, "categories": true, "filters": true, "worksets": true, "hiddenElements": false } }

python Manuals/Scripts/send_revit_command_durable.py --port <PORT> --command restore_view_state --params-file Work/<ProjectName>_<Port>/Logs/view_state_payload.json
```

Notes
- If a view template is assigned during restore and `apply.template=true`, category/filter values can be overwritten by the template. For fine control, restore with `template=false`, or clear the template first via `set_view_template(clear:true)`.
- Element-level hidden IDs can be large; only include when strictly necessary.

## Tips
- Pair with `get_elements_in_view(idsOnly)` before/after to validate visible sets.
- Keep per-view snapshots under `Codex/Work/Project_<port>_<timestamp>/` for traceability.
- Avoid repeated server calls by caching frequently used read results.


