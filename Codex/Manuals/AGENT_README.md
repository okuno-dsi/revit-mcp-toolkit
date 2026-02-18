# Agent Integration Guide

Purpose: enable any automation agent to connect to Revit MCP reliably and safely.

Critical Notes (Read First)
- Source-of-truth repo: use the GitHub clone as the current master.  
  If you keep rescue snapshots, treat them as reference-only and never overwrite the master without an explicit diff review.
- Read first: `Manuals/RevitMCP_Client_Dev_Guide.md` (client transport rules, queued handling, unwrap rules)
- Work rules: always use `Projects/<RevitFileName>_<docKey>/...` for all outputs, temp files, and scripts (no files directly under `Projects/`).
- Prefer canonical command names (`*.*` namespaced). Legacy names remain callable but are `deprecated` aliases.
- Prefer TaskSpec v2 for multi-step / write flows: the agent outputs a declarative `*.task.json` (no transport scripts) and executes via the fixed runner (`Design/taskspec-v2-kit/runner/mcp_task_runner_v2.py` or `.ps1`).
- For deterministic discovery (no LLM), use `help.suggest` first; then confirm details via `help.describe_command` before executing writes.
- `help.suggest` uses `glossary_ja.json` (override with `REVITMCP_GLOSSARY_JA_PATH`) and returns `suggestions[]` with safety flags (`writesModel`, `recommendedDryRun`).
- `list_commands` / `search_commands` default to returning canonical-only. Pass `includeDeprecated=true` only when you explicitly want legacy aliases in discovery results.
- If you receive an old/legacy command name, resolve it via capabilities:
  - Server: `GET /debug/capabilities` (canonical-only by default; add `?includeDeprecated=1` to include aliases; `?includeDeprecated=1&grouped=1` to group by canonical)
  - JSONL file: `docs/capabilities.jsonl` (1 line = 1 command)
  - Use `canonical` and `deprecated` fields to map legacy → canonical deterministically.
- Status command canonical is `revit.status` (aliases: `status`, `revit_status` are deprecated).
  - `revit.status` may reclaim stale `RUNNING/DISPATCHING` queue rows older than `REVIT_MCP_STALE_INPROGRESS_SEC` (default 21600 sec) to avoid “ghost running jobs” after crashes.
- Collaborative chat is available (server-local, no queue):
  - Methods: `chat.post`, `chat.list`, `chat.inbox.list`
  - Persistence (system-of-record): `<CentralModelFolder>\\_RevitMCP\\projects\\<docKey>\\chat\\writers\\*.jsonl`
  - Cloud models (ACC/BIM 360) are not supported for chat storage; chat is disabled when the active document is cloud-hosted.
    - `docKey` = stable per-project identifier (same as ViewWorkspace/Ledger ProjectToken). This avoids collisions when multiple `.rvt` exist in the same folder.
    - Fallback when `docKey` is missing: `projects\\path-<hash>\\...`
  - Root resolution: the server needs a one-time `docPathHint` (prefer central model path) and `docKey` (preferred). Revit Add-in auto-initializes this on ViewActivated; scripts/agents may pass both explicitly for the first call.
  - Invite convention: post to `ws://Project/Invites` and mention `@userId` (the add-in shows a non-blocking toast when mentioned).
  - Codex GUI: use the `Chat` button → Chat Monitor
    - Chat Monitor is read-only (compliance): it does not auto-run Codex and does not post AI replies back to chat.
    - Use `Copy→Codex` to copy the selected chat message and paste it into the main Codex GUI prompt (user clicks Send manually).

Essential Steps
- Read `ConnectionGuide/QUICKSTART.md` and `ConnectionGuide/PRIMER.jsonl`.
- Run `Scripts/Reference/test_connection.ps1 -Port 5210` to produce `Logs/agent_bootstrap.json`.
  - Alternatively set `REVIT_MCP_PORT` to override the port when `-Port` is omitted.
- Derive `activeViewId` from `Logs/agent_bootstrap.json` at `result.result.document.activeViewId` (or `result.result.environment.activeViewId` for legacy logs).
- List elements: `Scripts/Reference/list_elements_in_view.ps1 -Port 5210 -ViewId <activeViewId>` → writes `Logs/elements_in_view.json` at `result.result.rows`.
- Pick a valid `elementId (>0)`. Zero IDs must never be sent.
- For writes, prefer the safe scripts that preflight via `smoke_test`:
  - `Scripts/Reference/set_visual_override_safe.ps1 -Port 5210 -ElementId <id>`
  - `Scripts/Reference/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "..."`
  - Use `-DryRun` to generate payloads and skip network calls; use `-Force` to continue on `severity: warn`.
  - Parameter keys (write): commands accept `builtInId` → `builtInName` → `guid` → `paramName` (priority). Prefer `builtInId`/`builtInName`/`guid` for locale‑agnostic runs.

Policies & Guardrails
- Units: Length=mm, Angle=deg (server converts internally).
- ID-first: Resolve by IDs; avoid name-only execution when IDs are available.
- Zero-ID protection: scripts enforce non-zero IDs and stop with clear errors.
- smoke_test: required for writes; proceed only on `ok:true` (use `-Force` for `severity: warn`).
- JSON envelope: saved outputs include a JSON-RPC envelope; read values from `result.result.*` (`result.result.document.*` is the unified document context from `agent_bootstrap`).

Troubleshooting
- Port reachability: `Test-NetConnection localhost -Port 5210` must be True.
- IPv6 warnings are acceptable if IPv4 (127.0.0.1) succeeds.
- Timeouts on write: try a smaller target set, confirm active view permissions, and re-run. Scripts allow `--wait-seconds` tuning via underlying CLI.
- Add-in features: if `smoke_test` is not available, use read-only flows or consult `ConnectionGuide/smoke_test_guide.md`.
  - Docs inventory: `GET /docs/manifest.json` returns canonical-only by default; add `?includeDeprecated=1` to include deprecated aliases.

Chat troubleshooting
- `chat.*` returns `CHAT_HELPER_ROOT_UNKNOWN`: pass `docPathHint` once (prefer central model path), or open a Revit project with the add-in running (it auto-sets the root).
- Nothing shows in inbox: ensure your mention uses `@<RevitUsername>` and the invite is posted to `ws://Project/Invites`.

Local LLM (GGUF via llama.cpp)
- Minimal local agent: `..\..\NVIDIA-Nemotron-v3\tool\revit_llm_agent.py` (llama.cpp `llama-server.exe` + GGUF models).
  - Dry-run default (write commands are smoke-tested only). Use `--apply` to execute writes.
  - Uses `ping_server` / `agent_bootstrap` / `list_commands` instead of `context.get` / `catalog.export`.
- Example (Gemma 4B, GPU build if available):
  - `pushd ..\..\NVIDIA-Nemotron-v3\tool`
  - `python .\revit_llm_agent.py run --port 5210 --model ..\gguf\gemma-3-4b-it-q4_0.gguf --prompt "List the selected element IDs."`
  - Apply mode (writes): `python .\revit_llm_agent.py run --port 5210 --apply --force --prompt "..."`
  - Replay: `python .\revit_llm_agent.py replay --ledger ..\Projects\\<Project>_<Port>\out\ledger_*.jsonl`
  - `popd`

File Map (quick)
- Connection: `ConnectionGuide/QUICKSTART.md`, `ConnectionGuide/PRIMER.jsonl`
- Commands: `Commands/commands_index.json`, `Commands/HOT_COMMANDS.md`
- Scripts: `Scripts/Reference/*.ps1`, `Scripts/Reference/send_revit_command_durable.py`
- Logs: `Logs/*.json` (bootstrap, element lists, smoke/exec results)




