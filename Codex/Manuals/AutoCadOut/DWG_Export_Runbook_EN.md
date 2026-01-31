# DWG Export Runbook (Walls by Type, From Scratch)

Purpose
- Produce DWGs where the current view’s walls are split by type into separate files, then merged into one DWG with per‑type layer names. Includes state snapshot/restore to avoid visual side effects.

Prerequisites
- Revit + RevitMCP add‑in running on `5210` (verify: `Invoke-RestMethod http://127.0.0.1:5210/debug`).
- Optional: AutoCadMCP on `5251` (for automatic DWG merge via accoreconsole). If not running, the runbook still generates a `command.txt` you can submit later.
- Repo checked out at `VS2022/Ver431`.

One‑Shot (Recommended)
- Latest Project_* auto‑detected under `Codex/Work`:
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```
- Specify project folder explicitly:
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 `
  -ProjectDir "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\Project_5211_20251021_125656" `
  -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```
- Auto‑merge in AutoCAD (requires AutoCadMCP at 5251):
```
pwsh -File Codex/Manuals/Scripts/export_walls_by_type_snapshot.ps1 -Port 5210 -AutoMerge -Smoke -MaxWaitSec 120 -JobTimeoutSec 120
```

Outputs
- `Codex/Work/Project_…/DWG/seed.dwg` (walls hidden, non‑walls only)
- `Codex/Work/Project_…/DWG/walls_<TYPE>.dwg` (one per wall type)
- `Codex/Work/Project_…/DWG/command.txt` (AutoCAD merge payload)
- `Codex/Work/Project_…/DWG/Merged/walls_types_merged.dwg` (when -AutoMerge)

Optional (COM merge, AutoCAD running)
- If AutoCadMCP is not available, you can merge DWGs with AutoCAD COM:
  - Script: `Tools/AutoCad/merge_dwgs_by_map_com.py`
  - Dependency: `pywin32` (AI agent can help install: `python -m pip install pywin32`). No other external libraries.
  - Example:
```
python Tools/AutoCad/merge_dwgs_by_map_com.py \
  --source-dir "%USERPROFILE%\Documents\VS2022\Ver602\Codex\Work\dwg" \
  --out-dwg "%USERPROFILE%\Documents\VS2022\Ver602\Codex\Work\dwg\MERGED_DWG_COM.dwg" \
  --map-csv "%USERPROFILE%\Documents\VS2022\Ver602\Codex\Work\dwg\layermap.csv"
```

What the Script Does (High Level)
1) Save state: `save_view_state` on the active view.
2) Collect IDs in view: walls vs. non‑walls.
3) Export seed: hide all walls → `export_dwg` → `seed.dwg`.
4) For each wall type:
   - `show_all_in_view` (detach template + refresh) → hide non‑walls + other wall types (batched) → `export_dwg` → `walls_<TYPE>.dwg`.
5) Restore state: `restore_view_state`.
6) Optional merge: use AutoCadMCP `merge_dwgs_perfile_rename` to append the type name to the wall layer (e.g., `A‑WALL‑____‑MCUT_RC150`).

Performance & Safety Knobs
- `-Smoke`: runs `smoke_test` per write; stops on problems (use `-Force` to proceed on warnings).
- `-MaxWaitSec` / `-JobTimeoutSec`: per‑command client/server timeouts to avoid stalls.
- Server‑side batching (already built‑in): hide/override commands honor `batchSize`, `maxMillisPerTx`, `startIndex`, `refreshView`, `detachViewTemplate` for large sets.

Manual Checklist (if not using the script)
1) Save state:
```
python Codex/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command save_view_state --params "{}" --output-file Codex/Work/<ProjectName>_<Port>/Logs/view_state.json
```
2) Export seed:
- Hide walls (batched calls to `hide_elements_in_view`)
- `export_dwg { viewId, outputFolder: ".../DWG", fileName: "seed", dwgVersion: "ACAD2018" }`
3) For each wall type:
- `show_all_in_view { detachViewTemplate:true }` → hide non‑walls + other wall types → `export_dwg { fileName:"walls_<TYPE>" }`
4) Restore state:
- `restore_view_state { viewId, state, apply:{ template:true, categories:true, filters:true, worksets:true, hiddenElements:false } }`
5) Merge (later or via AutoCadMCP): submit `DWG/command.txt` to `http://127.0.0.1:5251/rpc`.

Notes
- Use idsOnly/countsOnly in reads to keep payloads light.
- If view templates block operations, `detachViewTemplate:true` is available on write commands.
- If AutoCAD merge is not online, keep `command.txt` and run it when ready.


## B/G Merge Quick Path (Core Console)

Purpose
- When you already have `seed.dwg`, `B.dwg`, and `G.dwg` (e.g., from beam‑filtered views), merge them into a single DWG where all B elements are on layer `B` and all G elements are on layer `G`.

Prerequisites
- AutoCAD 2026 Core Console installed: `C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe`.
- Export folder like `Codex/Work/AutoCadOut/Export_YYYYMMDD_HHMMSS` containing `seed.dwg`, `B.dwg`, `G.dwg`.

Run
```
pwsh -File Codex/Manuals/Scripts/merge_bg_from_seed.ps1 \
  -ExportDir "%USERPROFILE%\Documents\VS2022\Ver501\Codex\Work\AutoCadOut\Export_20251102_134250" \
  -Locale ja-JP
```

Outputs
- `Merged_B_G.dwg` in the same export folder.
- Core Console logs under `%TEMP%/CadJobs/MergeBG/<timestamp>/`.

Verification (optional)
- Dump layer names via Core Console to confirm the presence of `B` and `G` layers:
```
pwsh -File Codex/Manuals/Scripts/list_dwg_layers_coreconsole.ps1 \
  -DwgPath "%USERPROFILE%\Documents\VS2022\Ver501\Codex\Work\AutoCadOut\Export_20251102_134250\Merged_B_G.dwg" \
  -Locale ja-JP
```
- A `layers.txt` is written next to the DWG or reported in output.

Troubleshooting
- If the Core Console prompts unexpectedly (locale mismatch), pass `-Locale en-US`.
- The script writes a Shift‑JIS `.scr` and copies source DWGs to ASCII‑safe file names to avoid path/encoding issues.
- If you still see crashes using XREF+BIND scripts, prefer this INSERT+EXPLODE+entity‑relayer approach.

### Cleanup: lingering accoreconsole.exe
- Symptom: Core Console hangs or a previous run leaves `accoreconsole.exe` running.
- Quick kill (PowerShell):
  - `pwsh -File Codex/Manuals/Scripts/kill_accoreconsole.ps1`
  - List only: `pwsh -File Codex/Manuals/Scripts/kill_accoreconsole.ps1 -ListOnly`
- One‑liner alternative:
  - `Get-Process accoreconsole -ErrorAction SilentlyContinue | Stop-Process -Force`
- Notes:
  - Our merge scripts already enforce a timeout and force‑kill the child if it doesn’t exit.
  - If repeated hangs occur, try `-Locale en-US` and ensure the seed/output folders are writable and trusted.

### Cleanup: lingering acad.exe (full AutoCAD)
- Symptom: GUI AutoCAD remained open or zombie processes block file access.
- Quick kill (PowerShell):
  - `pwsh -File Codex/Manuals/Scripts/kill_cad_processes.ps1 -IncludeAcad`
  - List only: `pwsh -File Codex/Manuals/Scripts/kill_cad_processes.ps1 -ListOnly -IncludeAcad`
- One‑liner alternative:
  - `Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force`

### Collect logs for support
- Script (copies latest Core Console logs and scripts to the export folder):
  - `pwsh -File Codex/Manuals/Scripts/collect_accore_logs.ps1 -ExportDir ".../Export_YYYYMMDD_HHMMSS"`
  - Include layer-list jobs too: add `-IncludeLayerList`
- One‑liner (ad‑hoc):
  - `Get-ChildItem "$env:TEMP/CadJobs" -Recurse -Filter console_*.txt | Sort LastWriteTime -Descending | Select -First 10 | Copy-Item -Destination ".../Export_YYYYMMDD_HHMMSS/AccoreLogs" -Force`


