# RhinoMCP Template

Two-part scaffold:
- **RhinoMcpPlugin** (.NET 4.8, RhinoCommon): Rhino plug-in commands: `McpImport`, `McpCommitTransform`, `McpLockObjects`, `McpSettings`.
- **RhinoMcpServer** (.NET 6, Kestrel): JSON-RPC `/rpc` surface for MCP-style integration.

## Quick Start

### 1) Build RhinoMcpServer
```bash
cd RhinoMcpServer
dotnet restore
dotnet run
# server listens on default Kestrel port (configure with ASPNETCORE_URLS)
```

### 2) Build RhinoMcpPlugin
- Open `RhinoMcpPlugin.csproj` in Visual Studio 2022.
- Ensure `RhinoCommon.dll` HintPath matches your Rhino install.
- Restore NuGet packages (Newtonsoft.Json).
- Build. Load `.rhp` into Rhino (drag into Rhino window or use Plug-in Manager).

### 3) Try workflow
- Export mesh JSON from Revit (via get_instance_geometry).
- In Rhino: run `McpImport` and choose the JSON file.
- Move/rotate the imported block with Gumball.
- Run `McpCommitTransform` to send delta to Revit via `apply_transform_delta`.

## Notes
- Units: Rhino(mm) vs Revit(feet) handled via UnitUtil (304.8 factor).
- Scale/Shear are rejected by TransformUtil.
- Logs: `%LOCALAPPDATA%/RhinoMCP/logs`.
