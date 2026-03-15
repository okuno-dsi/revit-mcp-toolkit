# ExcelMCP Build and Release Flow

## Goals
- Build from source and release binaries must match.
- `/mcp` support must be verified from the freshly built source, not assumed from an older `Release` folder.
- `bin/` and `obj/` are build artifacts only and must not be treated as canonical source.

## Clean Build
1. `dotnet clean ExcelMCP.sln`
2. `dotnet build ExcelMCP.sln -c Release`
3. `dotnet test ExcelMCP.sln -c Release --no-build`

Or use:
- `pwsh .\publish_release.ps1`

If a local environment still shows duplicate assembly attributes or stale metadata, remove only project-local `bin/` and `obj/` and rerun the commands above.

## Release Verification Checklist
- `GET /health` returns `200`.
- `OPTIONS /mcp` returns `Allow: OPTIONS, GET, POST, DELETE`.
- `POST /mcp initialize` negotiates a supported protocol version.
- `POST /mcp notifications/initialized` returns `202 Accepted` with no JSON-RPC body.
- `POST /mcp tools/list` returns first-class `excel.*` tools, not only `excel.api_call`.
- `POST /mcp tools/call` succeeds for at least `excel.health` and `mcp.status`.

## MCP Protocol Baseline
- Default protocol version: `2025-11-25`
- Supported protocol versions:
  - `2025-11-25`
  - `2025-11-05`
  - `2025-03-26`
- Transport surface:
  - `OPTIONS /mcp`
  - `GET /mcp`
  - `POST /mcp`
  - `DELETE /mcp`

## Tool Registry
- File-based REST tools are loaded from `mcp_commands.jsonl`.
- COM-specific tools and MCP-only helper tools are registered in code.
- `excel.api_call` remains available as a fallback, but new clients should prefer first-class tools.

## Distribution Notes
- Package artifacts only after the clean build and integration tests pass.
- Do not copy stale `bin/Release` contents from an older source tree.
- When updating manuals or publishing binaries, update both the source tree and the release package from the same build output.
