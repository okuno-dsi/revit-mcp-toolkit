# DWG Layer Split Export (Generic)

Goal
- Export the current Revit view to DWG files where elements are split by a logical key (e.g., type, family, parameter) and then merged into a single drawing with per-group layer names.

Pattern (snapshot → per-group export → merge → restore)
1) Save view state so you can revert later.
2) For each target group (e.g., each wall type):
   - Show all, then hide non-target elements (and non-target categories if helpful).
   - Export DWG (As Displayed) to the project’s DWG folder.
3) Use AutoCAD Core Console (via AutoCadMCP) to merge the per-group DWGs.
4) Restore the view state.

Where results go
- Project DWG folder: `Codex/Work/Project_<port>_<timestamp>/DWG`
  - `seed.dwg` (non-target baseline)
  - `walls_<type>.dwg` per type (example)
  - `Merged/walls_types_merged.dwg` (final) when AutoMerge is enabled

Reference Script (Wall types)
- `Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1`
- What it does:
  - Captures the view state (`save_view_state`).
  - Hides all walls to export `seed.dwg`.
  - For each wall type: shows all, hides non-wall + other-wall types, exports `walls_<type>.dwg`.
  - Optionally merges outputs via AutoCadMCP (`merge_dwgs_perfile_rename`) to append the type name into the wall layer (e.g., `A-WALL-____-MCUT_RC150`).
  - Restores the view state (`restore_view_state`).

How to run (example)
- Use latest Project_* automatically and export walls split by type:
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210
```
- Specify a project folder explicitly:
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -ProjectDir "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\Project_5211_20251021_125656"
```
- Merge in AutoCAD automatically (requires AutoCadMCP running at 5251):
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -AutoMerge
```

Adapting to other categories / group keys
- Category: change include/exclude category IDs when collecting element IDs (e.g., Doors, Furniture). For walls the CategoryId is `-2000011`.
- Group key: in the script, grouping uses `typeName`. To split by family name, group by `familyName`. To split by a parameter, fetch parameter values first (e.g., via `get_param_values`) and group by that value.
- Layer rename pattern: adjust `rename.format` in the AutoCAD merge payload. The reference uses `{old}_{stem}`; you can change to `{stem}_{old}` or other.
- Export setup: `export_dwg` supports `useExportSetup` if you need a specific DWG export configuration.

Notes & Tips
- View templates can block operations. The reference uses `show_all_in_view` with `detachViewTemplate:true` on each iteration and restores the template at the end.
- For very large models, consider disabling `hiddenElements` re-application and rely on state restore + targeted hides.
- If AutoMerge isn’t available, keep the generated `command.txt` and run it later against AutoCadMCP.
- Optional COM merge (AutoCAD GUI running):
  - Script: `tools/AutoCad/merge_dwgs_by_map_com.py`
  - Dependency: `pywin32` (AI agent can help install: `python -m pip install pywin32`). No other external libraries.

Quick Runbook (Walls by Type)
This is a concrete, ready-to-run flow specialized for wall types. The generic pattern above remains reusable for other group keys.

- One-shot to the latest `Project_*` under `Codex/Work` (Revit MCP at 5210):
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```

- Explicit project folder:
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 `
  -ProjectDir "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\Project_5211_20251021_125656" `
  -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```

- Auto-merge in AutoCAD (AutoCadMCP at 5251):
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -AutoMerge -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```

Expected outputs under `.../DWG/`:
- `seed.dwg` (all walls hidden), `walls_<TYPE>.dwg` per wall type, `command.txt`, and `Merged/walls_types_merged.dwg` when auto-merged.
