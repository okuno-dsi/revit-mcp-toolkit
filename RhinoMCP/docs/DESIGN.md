# RhinoMCP Template – Design

This template scaffolds a two-part system:

- **RhinoMcpPlugin** (C#, .NET Framework 4.8, RhinoCommon): in-proc Rhino plug-in that imports Revit meshes, tracks baseline transforms, extracts deltas (T/R only), and talks to MCP servers.
- **RhinoMcpServer** (C#, .NET 6): out-of-proc lightweight JSON-RPC server (Kestrel) exposing `/rpc` with MCP-style methods for AI agents and tooling.

Core workflows:
1. Import Revit geometry (GLTF/OBJ-friendly JSON) into Rhino as Blocks with `RevitUniqueId` UserData.
2. Move/rotate in Rhino; detect delta vs. baseline (reject scale/shear).
3. Commit delta back to Revit via `apply_transform_delta` (Revit MCP).

See README.md for build & run.
