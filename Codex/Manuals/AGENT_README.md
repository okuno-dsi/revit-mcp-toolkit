# Agent Integration Guide

Purpose: enable any automation agent to connect to Revit MCP reliably and safely.

Essential Steps
- Read `ConnectionGuide/QUICKSTART.md` and `ConnectionGuide/PRIMER.jsonl`.
- Run `Scripts/test_connection.ps1 -Port 5210` to produce `Logs/agent_bootstrap.json`.
  - Alternatively set `REVIT_MCP_PORT` to override the port when `-Port` is omitted.
- Derive `activeViewId` from `Logs/agent_bootstrap.json` at `result.result.document.activeViewId` (or `result.result.environment.activeViewId` for legacy logs).
- List elements: `Scripts/list_elements_in_view.ps1 -Port 5210 -ViewId <activeViewId>` → writes `Logs/elements_in_view.json` at `result.result.rows`.
- Pick a valid `elementId (>0)`. Zero IDs must never be sent.
- For writes, prefer the safe scripts that preflight via `smoke_test`:
  - `Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id>`
  - `Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "..."`
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

Local LLM (GGUF via llama.cpp)
- Minimal local agent: `..\..\NVIDIA-Nemotron-v3\tool\revit_llm_agent.py` (llama.cpp `llama-server.exe` + GGUF models).
  - Dry-run default (write commands are smoke-tested only). Use `--apply` to execute writes.
  - Uses `ping_server` / `agent_bootstrap` / `list_commands` instead of `context.get` / `catalog.export`.
- Example (Gemma 4B, GPU build if available):
  - `pushd ..\..\NVIDIA-Nemotron-v3\tool`
  - `python .\revit_llm_agent.py run --port 5210 --model ..\gguf\gemma-3-4b-it-q4_0.gguf --prompt "List the selected element IDs."`
  - Apply mode (writes): `python .\revit_llm_agent.py run --port 5210 --apply --force --prompt "..."`
  - Replay: `python .\revit_llm_agent.py replay --ledger ..\Work\<Project>_<Port>\out\ledger_*.jsonl`
  - `popd`

File Map (quick)
- Connection: `ConnectionGuide/QUICKSTART.md`, `ConnectionGuide/PRIMER.jsonl`
- Commands: `Commands/commands_index.json`, `Commands/HOT_COMMANDS.md`
- Scripts: `Scripts/*.ps1`, `Scripts/send_revit_command_durable.py`
- Logs: `Logs/*.json` (bootstrap, element lists, smoke/exec results)

