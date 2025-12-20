# READ FIRST: RevitMCP 120% Quickstart (EN)

If you read just this one page, you can reliably **connect to the Revit MCP server**, **fetch data**, and **perform safe writes**. All commands below are copy‑paste ready.

## 0) Prerequisites & Defaults
- Revit is running and the MCP Add‑in is active (**default port:** `5210`).
- PowerShell 5+/7+ and Python 3.x are available.
- To override the port: set environment variable `REVIT_MCP_PORT` or pass `-Port`/`--port` on each script.

## 1) 60‑Second Connectivity Check (Required)
1) Port check
```powershell
Test-NetConnection localhost -Port 5210
```
2) Bootstrap (collect environment info and save it)
```powershell
pwsh -File Manuals/Scripts/test_connection.ps1 -Port 5210
```
- Output: `Work/<ProjectName>_<Port>/Logs/agent_bootstrap.json`
- Active View ID: `result.result.environment.activeViewId`
3) Optional Python ping
```bash
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server
```

## 2) Minimal Read‑Only Set (Safe)
- List element IDs in the **active view** (PowerShell)
```powershell
pwsh -File Manuals/Scripts/list_elements_in_view.ps1 -Port 5210
```
- Output: `Work/<ProjectName>_<Port>/Logs/elements_in_view.json` (IDs at `result.result.rows`)
- Python (IDs only, limit 200)
```bash
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params "{"viewId": <activeViewId>, "_shape":{"idsOnly":true,"page":{"limit":200}}}" --output-file Codex/Work/<ProjectName>_<Port>/Logs/elements_in_view.json
```

## 3) For Writes, Use the **Safe Scripts** (smoke_test → run)
1) Temporary visual override (color/alpha)
```powershell
# First do a DryRun to verify the payload
pwsh -File Manuals/Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id> -DryRun

# If ok, execute (use -Force if needed)
pwsh -File Manuals/Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id>
```
2) Safely update a wall parameter (recommend using **Comments**)
```powershell
pwsh -File Manuals/Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "Test via smoke" -DryRun
pwsh -File Manuals/Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "Test via smoke"
```
- Safety policy: run `smoke_test` first; only send the real command with `__smoke_ok:true` when smoke passes.
- Zero IDs are **forbidden**: do not send `viewId: 0` or `elementId: 0` (the scripts prevent this).

## 4) View Operations Best Practices (High Reliability, Non‑Blocking)
- Non‑destructive visual change flow:
  - Save: `save_view_state` → perform your visuals → Restore: `restore_view_state`
- On heavy views or views with templates, prefer the **updated parameters**:
  - `autoWorkingView:true` / `detachViewTemplate:true`
  - Split execution: `batchSize`, `maxMillisPerTx`, `startIndex`, `refreshView`
  - Loop rule: if response includes `nextIndex`, call the same method again with `startIndex=nextIndex` until `completed:true`.
- Example (single element color/alpha):
```bash
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command set_visual_override --params "{"elementId":<id>,"r":0,"g":200,"b":255,"transparency":20,"autoWorkingView":true,"__smoke_ok":true}"
```

## 5) Go‑To Commands (Ready Now)
- Connectivity: `ping_server`
- Environment: `agent_bootstrap` (active view ID, units, etc.)
- Levels: `get_levels` (there is a PS script with `-IdsOnly`)
- View elements: `get_elements_in_view` (supports `idsOnly` shape)
- Element info: `get_element_info` (use `rich:true` for details)
- View state: `save_view_state` / `restore_view_state`
- Visuals: `set_visual_override` (prefer updated params)
- Exporting: `export_dwg` (fails in Viewer mode by design)

## 6) Workflow Rules (Shortest Path, No Confusion)
- Create `Work/<ProjectName>_<ProjectID>` per project and store all artifacts/logs there.
- Reference docs live under `Manuals/`; edits and experiment logs go under `Work/`.
- Always prefer the `*_safe.ps1` for write operations (and start with `-DryRun`).
- Units: length=mm, angle=deg (server handles internal conversions).
- Read from `result.result.*` within the JSON‑RPC envelope.

## 7) Troubleshooting (Check Here First)
- HTTP 500 (enqueue): ensure request includes `"params": {}`. Revit may be busy → consider `/enqueue?force=1`.
- HTTP 409: another job is running → wait for completion or use `/enqueue?force=1` judiciously.
- No response while polling: check for modal dialogs in Revit.
- Viewer mode: if you see `Exporting is not allowed`, rerun in non‑viewer Revit.
- Ports check / conflicts:
```powershell
Get-NetTCPConnection -LocalPort 5210,5211,5212 -State Listen | Select-Object LocalPort,OwningProcess,@{N='Name';E={(Get-Process -Id $_.OwningProcess).ProcessName}}
```

## 8) Scale Further (Multi‑Revit & Recording/Replay)
- Stable multi‑instance chain: Client → Proxy(5221) → Playbook(5209) → RevitMCP(5210+)
- Routing pattern: `POST http://127.0.0.1:5221/t/{revitPort}/rpc`
- Details: `Manuals/ConnectionGuide/Revit_Connection_OneShot_Quickstart_EN.md`

## 9) Next Reads (Source Guides)
- Quickstart shortest path: `Manuals/ConnectionGuide/QUICKSTART.md`
- Hot commands digest: `Manuals/Commands/HOT_COMMANDS.md`
- Script list: `Manuals/Scripts/README.md`
- Updated visuals guide: `Manuals/UPDATED_VIEW_VISUALIZATION_COMMANDS_EN.md`

## 10) Execution Policy (Windows/PowerShell) — Important
- Unsigned `*.ps1` may be blocked. Use **Bypass** only for the run.
  - Example: `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/test_connection.ps1 -Port 5210`
  - Example: `pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/export_walls_by_type_simple.ps1 -Port 5210`
- See: `Manuals/ExecutionPolicy_Windows.md`

## 11) Fast DWG Export (Seed + Walls by Type)
- Avoid touching the current view; use `export_dwg { elementIds: [...] }` on a **duplicated** isolated view (high reliability).
- Example (auto‑detect latest project):
```powershell
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/export_walls_by_type_simple.ps1 -Port 5210
```

---
Follow this page to cover **connect → read → safe write → view control** end‑to‑end. If anything fails, check **7) Troubleshooting** and open the source guides as needed.
