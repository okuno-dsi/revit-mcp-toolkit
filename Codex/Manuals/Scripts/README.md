# Scripts

- test_connection.ps1: Ping and bootstrap; saves Work/<Project>_<Port>/Logs/agent_bootstrap.json
- test_terminology_routing.ps1: Validates terminology-aware `help.search_commands` ranking (断面/平断面/立面/RCP)
- list_elements_in_view.ps1: Uses activeViewId to list element IDs; saves Work/<Project>_<Port>/Logs/elements_in_view.json
- add_door_size_dimensions_in_active_view.ps1: Add associative door width/height dimensions (supports stacked offsets, side selection, optional overrides)
- get_family_instance_references_for_selection.ps1: List `FamilyInstanceReferenceType` references (stable strings) for the selected FamilyInstance (for advanced dimensioning)
- save_view_workspace.ps1: Save open views / active view (and zoom/camera best-effort) to a ledger-bound snapshot
- restore_view_workspace.ps1: Restore the saved view workspace snapshot (asynchronous via Idling; add -Wait to poll)
- set_view_workspace_autosave.ps1: Enable/disable autosave for the view workspace snapshot (and optionally toggle auto-restore)
- get_view_workspace_restore_status.ps1: Check progress/status of an in-progress restore
- set_visual_override.ps1: Applies a temporary visual override to chosen element
- set_visual_override_safe.ps1: Two-phase write (smoke_test preflight → execute with `__smoke_ok:true`). Use `-Force` to proceed on smoke `severity: warn`.
- update_wall_parameter_safe.ps1: Two-phase write to set a parameter (default `Param=Comments`, `Value=SmokeTest`). Resolves a valid elementId automatically if not provided. Use `-Force` to proceed on smoke `severity: warn`.
- export_walls_by_type_snapshot.ps1: Snapshot/restore flow to export DWGs split by wall type and (optionally) merge in AutoCAD (seed + per-type).

All scripts default to port 5210. Use `-Port` to override.
Alternatively, set environment variable `REVIT_MCP_PORT` to override the default when `-Port` is omitted.

Logs location
- Scripts now prefer `Work/<ProjectName>_<Port>/Logs/` for JSON outputs. They will fall back to `Manuals/Logs/` if needed for compatibility.

TaskSpec v2 (recommended for complex/write)
- Fixed runner (no free-form transport scripts): `Design/taskspec-v2-kit/runner/mcp_task_runner_v2.py` (Python) / `Design/taskspec-v2-kit/runner/mcp_task_runner_v2.ps1` (PowerShell).
- Store TaskSpec files under `Work/<Project>_<Port>/Tasks/*.task.json` (never under `Work/` root).
- RevitMCP transport: set `server` to `http://127.0.0.1:<PORT>/enqueue` (or base `http://127.0.0.1:<PORT>`); the runner polls `/job/{id}` automatically.

Safety and ID handling
- Scripts will not send `viewId: 0` or `elementId: 0`.
- `list_elements_in_view.ps1` reads the active view ID from `Work/<Project>_<Port>/Logs/agent_bootstrap.json` (fallback: `Manuals/Logs/agent_bootstrap.json`) at `result.result.environment.activeViewId`.
- `set_visual_override.ps1` chooses the first valid (>0) element from `elements_in_view.json` in `Work/<Project>_<Port>/Logs/` (fallback: `Manuals/Logs/`), or you can pass `-ElementId`.
- On invalid/missing IDs, scripts exit with an error before sending any command.
- For write operations, prefer `set_visual_override_safe.ps1` which runs `smoke_test` before the actual command.
- For updating parameters, use `update_wall_parameter_safe.ps1` (smoke_test → execute) and prefer a non-destructive field like `Comments` when testing.
- Safe scripts support `-DryRun` to write the would-be payloads to `Logs/*.dryrun.json` and skip network calls.

- Durable sender: send_revit_command_durable.py (jobId polling; default in scripts)
- Legacy FIFO sender has been removed from this folder

DWG Layer Split (generic)
- See: `Manuals/AutoCadOut/DWG_Layer_Split_Export_EN.md` for the generic pattern.
- Wall-by-type example to latest Project_* folder:
  `pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210`

Schedule CSV Exports
- export_all_schedules_to_csv.ps1: Bulk-export all schedules to CSV.
  - As‑Is (as-displayed):
    `pwsh -ExecutionPolicy Bypass -File Manuals/Scripts/export_all_schedules_to_csv.ps1 -Port 5210 -OutDir "Work/<Project>_<Port>/Schedules_Compare/AsIs"`
  - Itemize + Fill (value semantics):
    `pwsh -ExecutionPolicy Bypass -File Manuals/Scripts/export_all_schedules_to_csv.ps1 -Port 5210 -OutDir "Work/<Project>_<Port>/Schedules_Compare/Itemize_Fill" -FillBlanks -Itemize`
  - Notes: enable “アイテムを個別に表示” and add a visible “要素ID” column in the schedule for best results.

CSV encoding (Required)
- 日本語を含むCSVを扱う場合、文字コードは次のいずれかを必須とします。
  - UTF-8 BOM付き（推奨）
  - Shift-JIS（CP932）
- 目安コマンド例:
  - PowerShell 7+: `Export-Csv ... -Encoding utf8BOM`
  - Windows PowerShell 5.x: `Export-Csv ... -Encoding UTF8`（BOM付き）
  - Shift-JISで保存したい場合（OSの既定がCP932の場合）: `Export-Csv ... -Encoding Default`
    - もしくは `Out-File -Encoding Shift_JIS` を用いる（内容がCSV文字列の場合）
- ツールやExcelでの文字化け防止のため、上記いずれかのエンコードで保存してください。

