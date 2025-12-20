# Manuals Overview

This folder is the curated entry point for Revit MCP work: guides, commands, helper scripts, and logs.

## Ready-to-Start (Start Here)
- Read: `START_HERE.md` at repo root for the shortest path to begin.
- Quickstart: `Manuals/ConnectionGuide/QUICKSTART.md` (port check, ping, bootstrap).
- Work rules: `WORK_RULES.md` (use `Work/<ProjectName>_<ProjectID>/` for per‑project work).
- Helper scripts (PowerShell):
  - `Manuals/Scripts/test_connection.ps1` — connectivity + bootstrap
  - `Manuals/Scripts/list_elements_in_view.ps1` — element IDs to `Work/<ProjectName>_<Port>/Logs/elements_in_view.json`
  - Safe writes (two‑phase):
    - `Manuals/Scripts/set_visual_override_safe.ps1`
    - `Manuals/Scripts/update_wall_parameter_safe.ps1`
  - Resilient view ops:
    - `Manuals/Scripts/hide_elements_resilient.ps1` — time‑sliced hide with detachTemplate/startIndex/nextIndex
- Port override: set `$env:REVIT_MCP_PORT = <PORT>` or pass `-Port` to scripts.

Quick view ops
- Rename a view: use `rename_view` with `{ viewId, newName }`.
- Save/restore templates and categories: `save_view_state` / `restore_view_state`.

## Structure
- ConnectionGuide: core connection and smoke‑test docs, curated guides, quickstart
- Commands: command index and references
- Scripts: helper scripts for connection and listing elements
- Logs: moved under `Work/<ProjectName>_<ProjectID>/Logs` (Manuals/Logs is deprecated)

For detailed guidance, see `Manuals/ConnectionGuide/QUICKSTART.md` and `Manuals/ConnectionGuide/INDEX.md`.

## Command registration notes
- After updating/rebuilding the Revit MCP add‑in, restart Revit (or reload the add‑in) so new commands are registered and visible to clients.
- Always verify command availability with `list_commands` (prefer `namesOnly:true`). Save the list to `Work/<ProjectName>_<Port>/Logs/list_commands_names.json` for reproducibility.
- If a manual references a command you don’t see, confirm:
  - The port/instance is correct (`$env:REVIT_MCP_PORT` / `-Port`).
  - Your build registers the handler in `RevitMcpWorker`.
  - Warm‑up with `ping_server` → `agent_bootstrap` → `list_commands` to let Revit initialize.

## Recent Updates (View/Visualization)
- Non‑blocking execution patterns and parameters for view/visualization commands:
  - `Manuals/UPDATED_VIEW_VISUALIZATION_COMMANDS_EN.md`
  - `Manuals/updated_view_visualization_commands.en.jsonl`

## Recent Updates (Revisions and Reads)
- Revisions: see `Manuals/RevisionOps.md` for listing, updating, cloud creation, and the new circle helper (`create_revision_circle`).
- Parameters (Bulk): see `Manuals/Commands/Bulk_Parameters_EN.md` for `get_type_parameters_bulk` and `get_instance_parameters_bulk` with SI‑normalized outputs.
  - For many elements sharing the same parameter (e.g., Rooms grouped by `積載荷重`), prefer `get_instance_parameters_bulk` over looping `get_*_params` to cut down JSON‑RPC round‑trips.
- View Typing: see `Manuals/Commands/GetTypesInView_Command_EN.md` for `get_types_in_view` to discover type usage quickly per view.
- Geometry (Analyze): see `Manuals/Commands/Analyze_Segments_EN.md` for `analyze_segments` (2D/3D line/point relations).
- Geometry (Batch Mesh): see `Manuals/Commands/Get_Instances_Geometry_EN.md` for `get_instances_geometry` (multi-category mesh export with analytic fallbacks for rooms/curves/points).
- Join Geometry: see `Manuals/Commands/Join_Geometry_EN.md` for `join_elements`, `unjoin_elements`, `are_elements_joined`, `switch_join_order`, `get_joined_elements`.
- Lightweight reads: see `Manuals/ShapeAndPaging.md` for `_shape.idsOnly`, `_shape.page.limit/skip`, and `summaryOnly` options now available across many get* commands (Doors/Floors/CurtainWalls/Roofs/Materials/Stairs/Railing/Ceilings/Mass/In‑Place/FamilyInstances/Architectural Columns/Face).

## Parameter Keys (Write Commands)
- All parameter‑write commands (`update_*_parameter`, `set_*_parameter`, `*_type_parameter`, `set_view_parameter`) accept parameter keys in priority order:
  1) `builtInId:int` (BuiltInParameter numeric)
  2) `builtInName:string` (BuiltInParameter enum name)
  3) `guid:string`
  4) `paramName:string` (localized display name)
- Backward compatible: existing `paramName` payloads continue to work. Prefer `builtInId` / `builtInName` / `guid` for locale‑agnostic workflows.

## Important (Windows/PowerShell)
- Unsigned scripts may be blocked by Execution Policy. Prefer process‑scoped bypass when running scripts:
  - `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/test_connection.ps1 -Port 5210`
- Details: `Manuals/ExecutionPolicy_Windows.md`

## CSV Encoding (Required for Japanese)
- 日本語を含むCSVファイルは、次のいずれかの文字コードで保存してください（必須）。
  - UTF-8 BOM付き（推奨）
  - Shift-JIS（CP932）
- 理由: Excelや各種ツールでの文字化け防止のため。
- PowerShell例:
  - PowerShell 7+: `Export-Csv ... -Encoding utf8BOM`
  - Windows PowerShell 5.x: `Export-Csv ... -Encoding UTF8`（BOM付き）
  - Shift-JIS（OS既定がCP932の場合）: `Export-Csv ... -Encoding Default`

## DWG Exports (fast path)
- Simple walls‑by‑type export without tweaking current view (uses `export_dwg { elementIds: [...] }`):
  - `Manuals/Scripts/export_walls_by_type_simple.ps1`
- Batch export multiple views in one call (uses `export_dwg { items: [...] }` with optional `startIndex/batchSize/maxMillisPerTx`):
  - See Runbook: `Manuals/Runbooks/View_Isolation_and_DWG_Plan_EN.md` (section 4.1) for items[] examples and time‑sliced loop.

## AutoCAD Merge Helpers
- Merge B/G DWGs into one file using Core Console (layers consolidated to `B` and `G`):
  - `Manuals/Scripts/merge_bg_from_seed.ps1`
  - Usage example:
    - `pwsh -File Codex/Manuals/Scripts/merge_bg_from_seed.ps1 -ExportDir "Codex/Work/AutoCadOut/Export_YYYYMMDD_HHMMSS" -Locale en-US`
- Verify layers of any DWG via Core Console:
  - `Manuals/Scripts/list_dwg_layers_coreconsole.ps1`
  - Usage example:
    - `pwsh -File Codex/Manuals/Scripts/list_dwg_layers_coreconsole.ps1 -DwgPath ".../Merged_B_G.dwg" -Locale en-US`

## Snapshots
- Save current project snapshot (project info, open docs/views, sample element IDs):
  - `pwsh -ExecutionPolicy Bypass -File Manuals/Scripts/save_project_snapshot.ps1 -Port 5210 -OutRoot Work/Snapshots -MaxViews 5`

- Quick Reference: see `Manuals/Quick_Reference_Recipes_EN.md` for high-performance recipes (parameters, type changes, transforms, baselines/geometry, joins).

## Editing Policy (Schedules)
- Do not edit schedule cells directly via MCP — performance is poor and workflows become fragile.
- Instead, update the underlying element parameters that the schedule displays (e.g., use `set_room_param`, `update_wall_parameter`). The schedule will reflect the parameter changes.
- Prefer adding a visible "Element ID" column and, where possible, target by `UniqueId` for robustness. See: `Manuals/Schedule_Exports_Guide_JA.md`.

## Exports
- Dashboard (project + levels + rooms + categories/types): `export_dashboard_html`
- Schedules: `export_schedule_to_csv`, `export_schedule_to_excel` (stable)
- See: `Manuals/Schedule_Exports_Guide_JA.md` for schedule export usage.


