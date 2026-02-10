Revit MCP Codex – Quick Notes

- Default port: `5210` (set `$env:REVIT_MCP_PORT` to override)

Quickstart
- Test connection: `Scripts/Reference/test_connection.ps1 -Port 5210`
- Get levels: `Scripts/Reference/get_levels.ps1 -Port 5210`
  - IDs only: `Scripts/Reference/get_levels.ps1 -Port 5210 -IdsOnly`
- Results save under `Projects/<ProjectName>_<Port>/Logs/` (e.g., `levels.json`).
- Quick check: `Scripts/Reference/test_connection.ps1 -Port 5210`
- Durable command sender (recommended):
  - `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command ping_server`
- Project working root: `Projects/<ProjectName>_<ProjectID>`
- Manuals folder is for guides only; save runtime outputs under `Projects/` per project.
- Use safe scripts for writes: `*_safe.ps1` (supports `-DryRun`)

See `Docs/Manuals/` for detailed guides and command references.
Revision features: see `Docs/Manuals/RevisionOps.md` for listing, updating, cloud, and circle helpers.
Lightweight reads: see `Docs/Manuals/ShapeAndPaging.md` for `_shape`/`summaryOnly`.

CSV encoding (Required when using Japanese)
- If a CSV contains Japanese text, save it as either:
  - UTF-8 with BOM (preferred), or
  - Shift-JIS (CP932)
- This avoids mojibake in Excel and downstream tools. In PowerShell:
  - PowerShell 7+: `Export-Csv ... -Encoding utf8BOM`
  - Windows PowerShell 5.x: `Export-Csv ... -Encoding UTF8` (adds BOM)
  - For Shift-JIS (when system code page is 932): `Export-Csv ... -Encoding Default`

Command availability & registration (important)
- Source of truth: run `list_commands` (prefer `namesOnly:true`) to confirm what this Revit add-in actually exposes on your machine/port.
- After updating/rebuilding the add-in, restart Revit (or reload the add-in) so the command registry is refreshed. A running Revit instance will not auto‑load new handlers.
- If manuals mention a command that you don’t see, check:
  - You are talking to the intended port/instance (`$env:REVIT_MCP_PORT` / `-Port`).
  - Your add‑in build includes the handler (RevitMCPAddin → `RevitMcpWorker` registers commands like `RenameTypesBulkCommand`, etc.).
  - Warm‑up: `ping_server` → `agent_bootstrap` → `list_commands` again (first call may be slower while Revit initializes).
- Save the current registry for auditing:
  `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command list_commands --params '{"namesOnly":true}' --output-file Projects/<Project>_<Port>/Logs/list_commands_names.json`




