# RhinoMCP – Implementation Overview (MVP)

This document summarizes the implemented RhinoMCP MVP so that AI agents and future contributors can quickly understand what exists, what works end‑to‑end, and what remains to expand. It is optimized for machine and human reading when driving subsequent incremental changes.

## Objective

Enable translation+rotation (no scale/shear) adjustments of Revit instances using Rhino. Rhino imports Revit geometry as block instances, users move/rotate in Rhino, and the delta transform is committed back to Revit.

## Components and Ports

- `RhinoMcpServer` (out‑of‑proc, .NET 6, Kestrel HTTP)
  - Listens on `http://127.0.0.1:5200`
  - Exposes JSON‑RPC endpoint `/rpc`
  - Logs per‑request JSON lines to `%LOCALAPPDATA%/RhinoMCP/logs/RhinoMcpServer.log`

- `RhinoMcpPlugin` (in‑proc, .NET Framework 4.8, Rhino 7)
  - Listens as an IPC HTTP endpoint for the server at `http://127.0.0.1:5201/rpc`
  - On load, starts the IPC listener; optionally attempts to start the server process
  - Writes plugin logs to `%LOCALAPPDATA%/RhinoMCP/logs/RhinoMcpPlugin.log`

- `Revit MCP` (existing, out‑of‑proc)
  - Base URL default: `http://127.0.0.1:5210`

## Data Model

Import snapshot (from Revit MCP):

```json
{
  "uniqueId": "string",
  "transform": [[...],[...],[...],[...]],
  "units": "feet",
  "vertices": [[x,y,z], ...],
  "submeshes": [{"materialKey":"...", "intIndices":[...]}, ...],
  "materials": [],
  "snapshotStamp": "ISO8601",
  "geomHash": "optional"
}
```

Stored on the Rhino side (as UserData on instance attributes):

```
RevitUniqueId, BaselineWorldXform, Units, ScaleToRhino, SnapshotStamp, GeomHash
```

## Core Flows

1) Import (Revit → Rhino)
- Server receives `rhino_import_snapshot`
- Forwards to plugin IPC → plugin creates a block definition and places an instance
- UserData is attached (attributes) with Revit metadata and baseline xform

2) Selection preview (Rhino)
- Server receives `rhino_get_selection`
- Plugin enumerates selected Rhino objects with `RevitRefUserData`, computes delta `Δ = inv(Baseline) * CurrentInstanceXform`
- Delta is validated as translation+rotation only (no scale/shear)

3) Commit (Rhino → Revit)
- Server receives `rhino_commit_transform`
- Plugin reads selected instances, extracts T (mm) and yaw (deg), converts to feet, and POSTs `apply_transform_delta` to Revit MCP (5210)

4) Lock / Unlock
- `rhino_lock_objects` / `rhino_unlock_objects`: lock state set via Rhino object table API; either by `uniqueIds` or current selection

5) Import by Revit UniqueIds
- `rhino_import_by_ids`: server calls Revit MCP `get_instance_geometry` for each ID, then forwards to plugin as `rhino_import_snapshot`

## JSON‑RPC Methods

All requests are POSTed to `http://127.0.0.1:5200/rpc` with a standard JSON‑RPC 2.0 envelope. The server always returns HTTP 200 and places application errors in the JSON‑RPC `error` object.

- `rhino_import_snapshot` → Import one element snapshot into Rhino
  - params: snapshot JSON (see Data Model)
  - result: `{ ok: bool, msg: string }`

- `rhino_get_selection` → List selected RevitUniqueIds and deltas
  - params: `{}`
  - result: `{ ok: true, items: [{ uniqueId, delta: { translate{x,y,z,units}, rotateZDeg }, guard: { snapshotStamp, geomHash } }] }`

- `rhino_commit_transform` → Commit deltas of current selection back to Revit MCP
  - params: `{}`
  - result: `{ ok: bool, appliedCount: number, errors: number }`

- `rhino_lock_objects` / `rhino_unlock_objects` → Lock/unlock by ids or selection
  - params: `{ uniqueIds?: string[] }`
  - result: `{ ok: true, count: number }`

- `rhino_refresh_from_revit` → Placeholder for future sync from Revit
  - params: `{}`
  - result: stubbed `{ ok: true, msg }`

- `rhino_import_by_ids` → Import by Revit UniqueIds (pull geometry from Revit MCP)
  - params: `{ uniqueIds: string[], revitBaseUrl?: string }`
  - result: `{ ok: boolean, imported: number, errors: number }`

Unknown methods return `error.code = -32601`.

## Transform Delta Extraction Rules

- Translation is read from matrix elements (m03, m13, m23) in mm space.
- Rotation is validated by checking linear 3×3 part is approximately orthonormal (unit columns, orthogonal pairs, |det|≈1). Yaw (Z‑axis rotation) is computed from rotated X axis.
- Any scale/shear causes rejection.

## Scripts and Test Data

- `scripts/start_server.ps1` → build and start server on a given URL (default 5200)
- `scripts/test_rpc.ps1` → health check, unknown method, import minimal test element, get selection, and commit
- `testdata/snapshot_min.json` → minimal triangle mesh snapshot for import testing

## Logging and Fault Tolerance

- Server logs JSON lines: `{ time, id, method, ok, msg }` (one per request)
- HTTP status is always 200; app errors are encoded in the JSON‑RPC error payload
- Known pitfall: on Windows, running `dotnet` instances can lock `bin` output, preventing rebuild; kill/stop the running process before rebuilding

## Current State and Verified E2E

- Import snapshot: OK (first time) → Rhino instance created, metadata attached
- Get selection: OK → returns delta (0,0,0 / 0° when unchanged)
- Commit: OK after Revit MCP is up → `appliedCount: 1`
- Lock/unlock: OK
- Import by IDs: Implemented (requires `get_instance_geometry` in Revit MCP; tested pattern)

## Known Gaps / Next Steps

- Refresh from Revit: complete the `rhino_refresh_from_revit` flow (pull latest + replace/update in Rhino)
- Import re‑runs: if a block definition already exists, reuse/replace instead of failing
- Rotation generalization: support full 3D rotations (beyond yaw) or local axes
- Packaging: installer and settings UI for the plugin; simplified distribution
- Hardening: retry/timeout policy for IPC and MCP calls; better error surfaces in results

## Contributor Notes

- Ports (server/plugin/Revit): 5200 / 5201 / 5210; update in `RhinoMcpServer/Program.cs`, `RhinoMcpServer/Rpc/Rhino/PluginIpcClient.cs`, and `RhinoMcpPlugin/Core/PluginIpcServer.cs` if needed
- Add a new server RPC
  1) Create handler under `RhinoMcpServer/Rpc/Rhino/*.cs`
  2) Map it in `RpcRouter.cs`
  3) If it needs Rhino context, forward to plugin IPC at 5201
- Rhino metadata lives on instance attributes (UserData). Use `RevitRefUserData.From(RhinoObject)` to lookup.
- Baseline vs current transform: baseline stored at import time; current is `InstanceObject.InstanceXform`

## File Map (key files)

- Server
  - `RhinoMcpServer/Program.cs` (Kestrel + `/rpc`)
  - `RhinoMcpServer/Rpc/RpcRouter.cs` (method dispatch)
  - `RhinoMcpServer/Rpc/Rhino/*.cs` (command handlers)
  - `RhinoMcpServer/Rpc/Rhino/PluginIpcClient.cs` (IPC forwarder → 5201)
  - `RhinoMcpServer/Rpc/RevitProxy/RevitMcpClient.cs` (HTTP client → 5210)

- Plugin
  - `RhinoMcpPlugin/Plugin.cs` (load/start/stop, default URLs)
  - `RhinoMcpPlugin/Core/PluginIpcServer.cs` (IPC listener on 5201)
  - `RhinoMcpPlugin/Core/TransformUtil.cs` (TR‑only extraction)
  - `RhinoMcpPlugin/Core/UserData/RevitRefUserData.cs` (metadata)
  - `RhinoMcpPlugin/Commands/*.cs` (Rhino commands)

- Scripts / Data
  - `scripts/start_server.ps1`, `scripts/test_rpc.ps1`
  - `testdata/snapshot_min.json`

