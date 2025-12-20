# Revit MCP Connection Quickstart (Port 5210)

- Verify port: `Test-NetConnection localhost -Port 5210` => TcpTestSucceeded True
- Ping: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server`
- Bootstrap: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command agent_bootstrap --output-file Logs/agent_bootstrap.json`
 - Tip: You can set `REVIT_MCP_PORT` to override the default port when scripts are run without `-Port`.
- List elements in active view (ids):
  `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_elements_in_view --params "{\"viewId\": <activeViewId>, \"_shape\":{\"idsOnly\":true,\"page\":{\"limit\":200}}}" --output-file Logs/elements_in_view.json`
- Save/Restore view state when changing visibility:
  - Save: `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command save_view_state --params "{}" --output-file Logs/view_state.json`
  - Restore: prepare a payload with the captured `state` and run `restore_view_state`.
- Optional write (requires add-in support and confirmation):
  1) Preflight: `Manuals/Scripts/set_visual_override_safe.ps1 -Port 5210 -ElementId <id>`
  2) The script runs `smoke_test` then executes with `__smoke_ok:true`.
  - Parameter update (safe): `Manuals/Scripts/update_wall_parameter_safe.ps1 -Port 5210 -ElementId <id> -Param Comments -Value "Test via smoke"`
  - Create Room (Durable): `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command create_room --params '{"levelName":"1FL","x":1500,"y":1500,"__smoke_ok":true}'`
    - Defaults: `autoTag=true`, `strictEnclosure=true`, `checkExisting=true`; response may be `mode:"existing"` with `elementId` when a room already covers the point.

- Duplicate active view safely (idempotent):
  - Resolve current view id/name, then:
    `python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command duplicate_view --params '{"desiredName":"<Base> Copy","onNameConflict":"returnExisting","idempotencyKey":"dup:<baseUid>:<Base> Copy"}'`
  - For a specific view: include `viewId` in params.

Notes
- Some environments may not expose `smoke_test`; fallback to read-only steps and explicit `__smoke_ok:true` for writes as permitted.
- Default units: Length=mm, Angle=deg. Server converts internally.
- Important: Never send `viewId: 0` or `elementId: 0`.
  - `agent_bootstrap.json` saved by Scripts/test_connection.ps1 is a JSON-RPC envelope.
  - Read the active view ID from `result.result.environment.activeViewId`.
  - `Logs/elements_in_view.json` stores rows at `result.result.rows`.
  - The helper scripts validate IDs and will stop with a clear error if an ID is missing/invalid.
 - Safe write execution: prefer the `*_safe.ps1` scripts, which run `smoke_test` first. Use `-DryRun` to preview payloads, `-Force` to continue on warnings.




