# Work Rules

- Root work folder: `<Revit_MCP Root>\Projects` (from `%LOCALAPPDATA%\RevitMCP\paths.json` → `workRoot`).
- For each Revit project, create a subfolder named `<ProjectName>_<ProjectID>` under that `Projects` root.
- Perform all per-project scripts, logs, and outputs within that subfolder.
- Keep Projects/ focused on project artifacts only (exports, snapshots, logs, notes). Do not place source code or manuals here.
- Prefer `Projects/<ProjectName>_<ProjectID>/Logs/` for runtime JSON/CSVs instead of `Docs/Manuals/Logs/` when possible.
- Use `Docs/Manuals/` for guides/references. Use `Scripts/Reference/` for runnable reference scripts; do not edit originals under `Docs/Manuals/`.
- Non-essential or legacy docs may be relocated to `Trash/` to reduce clutter. Active usage docs remain under `Docs/Manuals/`.
- Prefer safe scripts (`*_safe.ps1`) for writes (smoke_test → execute). Use `-DryRun` to preview payloads.
- Never send `viewId: 0` or `elementId: 0`. Scripts enforce this and will stop on invalid IDs.

## Root override (optional)
- Default root is `%USERPROFILE%\Documents\Revit_MCP` (legacy: `Codex_MCP`).
- To change roots without code edits, create:
  - `%LOCALAPPDATA%\RevitMCP\paths.json`
  - Example (recommended):
    ```json
    {
      "root": "C:\\\\Users\\\\<user>\\\\Documents\\\\Revit_MCP",
      "codexRoot": "C:\\\\Users\\\\<user>\\\\Documents\\\\Revit_MCP\\\\Codex",
      "workRoot": "C:\\\\Users\\\\<user>\\\\Documents\\\\Revit_MCP\\\\Projects"
    }
    ```
  - Do **not** use `...\Revit_MCP\Codex\Projects` as a work root. Always use `...\Revit_MCP\Projects`.

## Session/Startup discipline (must-do)
- At the start of each session (or when switching projects), explicitly set the target Work project folder and port:
  - Create/choose `Projects/<ProjectName>_<ProjectID>/` (or `Projects/<ProjectName>_<Port>/` when ID is unknown) and keep all outputs under it.
  - Ensure `$env:REVIT_MCP_PORT` or `-Port` matches the Revit instance you are driving.
- Always save connectivity/bootstrap and command inventory to the current project’s Logs:
  - `Scripts/Reference/test_connection.ps1 -Port <PORT>` → `Projects/<ProjectName>_<Port>/Logs/agent_bootstrap.json`
  - `list_commands (namesOnly:true)` → `Projects/<ProjectName>_<Port>/Logs/list_commands_names.json`
- Do not reuse another project’s Logs folder between sessions; create a new project folder if needed.
- When switching Revit instances/ports, also switch the Work project folder accordingly (one Work root per active project/port).

## Naming & hygiene
- Use stable `<ProjectName>_<ProjectID>` keys when available; if not, prefer `<ProjectName>_<Port>` as a temporary key.
- Keep `Logs/` lightweight but complete: inputs, payloads (when safe), responses, and summaries for reproducibility.
- Avoid writing into `Docs/Manuals/Logs/` except for compatibility tests; migrate to `Projects/<ProjectName>_<...>/Logs/`.
- For batch runs, prefix outputs with timestamps (e.g., `*_YYYYMMDD_HHMMSS.json`) to avoid overwrites.




