# Rebar Prototype Workflow (Hooks + Rotation)

Goal: standardize a repeatable “prototype” workflow for fast reinforcement iteration via Revit MCP.

This runbook assumes:
- Revit is running and Revit MCP server is listening (e.g. port `5210`).
- You run scripts from the Codex workspace root.
- If the project has `RebarBarType` / `RebarShape` count = 0, auto-rebar cannot create rebars.
  - Run `list_rebar_bar_types` → if `count:0`, run `import_rebar_types_from_document` (best-effort) from a structural template.

## 1) Regenerate tool-tagged rebars on selected hosts

Select one or more hosts (Structural Columns / Structural Framing), then run:

```powershell
python Scripts/Reference/send_revit_command_durable.py --port 5210 --command rebar_regenerate_delete_recreate --params "{""useSelectionIfEmpty"":true,""deleteMode"":""tagged_only"",""tag"":""RevitMcp:AutoRebar"",""options"":{""tagComments"":""RevitMcp:AutoRebar""}}"
```

Notes:
- This is a delete+recreate command. It only deletes rebars within the host(s) and matching the tag in `Comments`.
- Host transactions are separated; failing hosts roll back without affecting others.

## 2) Apply hook settings (start/end hook + rotations)

### Option A (recommended): copy from a reference rebar

1. Select a **reference** rebar first (the one you already adjusted in UI and is “correct”).
2. Also select the target rebars.
3. Run:

```powershell
python Scripts/Reference/rebar_set_hooks_and_rotations_on_selection.py --port 5210
```

Behavior:
- Uses the first selected element as the reference (unless `--reference-id` is provided).
- Copies these 4 instance parameters to all other selected rebars:
  - `始端のフック` (Start Hook)
  - `終端のフック` (End Hook)
  - `始端でのフックの回転` (Start Hook Rotation)
  - `終端でのフックの回転` (End Hook Rotation)

### Option B: force a specific hook type ID and rotations

If you already know the `RebarHookType` elementId:

```powershell
python Scripts/Reference/rebar_set_hooks_and_rotations_on_selection.py --port 5210 --hook-type-id 4857530 --start-rot-deg 0 --end-rot-deg 180
```

Important: Revit may normalize `180°` to `0°` for some angle parameters.  
This script applies a safe workaround by setting `179.999°` when `180°` is requested.

## 3) Verify quickly

The script automatically verifies and logs the result under:
- `Projects/RevitMcp/<port>/rebar_hooks_apply_*.json`

## Next: align host parameters ↔ rebar behavior

Before building a “parameter-to-rebar” validation table, it’s recommended to standardize:
- which host attributes come from Type vs Instance parameters (via `RebarMapping.json`)
- which options override mapping (`rebar_plan_auto.options`)

### Quick mapping dump (selected hosts)
Select host(s) and run:

```powershell
python Scripts/Reference/rebar_mapping_dump_selected_hosts.py --port 5210 --include-debug
```

Then compare:
- `rebar_mapping_resolve` results
- `rebar_plan_auto` (planned curves/layout)
- actual created rebars + their parameters/layout




