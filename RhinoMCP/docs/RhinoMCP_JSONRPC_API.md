# RhinoMCP JSON‑RPC API Reference

This document specifies the JSON‑RPC interface exposed by RhinoMcpServer (5200) and the semantics each method implements across the Rhino plugin (5201) and Revit MCP (5210).

Base URL: `http://127.0.0.1:5200`

- Endpoint: `POST /rpc`
- Content‑Type: `application/json; charset=utf-8`
- Envelope (JSON‑RPC 2.0): `{ "jsonrpc":"2.0", "id": Any, "method": String, "params": Object }`
- HTTP code: Always 200 (errors are returned in `error` payload)

Error policy
- Unknown method → `error.code = -32601`, `message = "Unknown method: ..."`
- Application/IPC/serialization errors → `error.code = -32000` or `-32001` with details in `message`

Conventions
- Units: Rhino works in mm; Revit in feet. Incoming Revit geometry (feet) is scaled to mm on import; outgoing translations are converted back to feet.
- Transform: Only translation + rotation (yaw around Z) are accepted. Any scale/shear is rejected.

## Methods

### rhino_import_snapshot
- Purpose: Import one Revit element snapshot into Rhino as a Block instance
- Direction: Server → Plugin (IPC)
- Params: Snapshot object
  - See the Design data model: `uniqueId`, `transform` (4x4), `units` (feet), `vertices`, `submeshes[{materialKey,intIndices}]`, `materials[]`, `snapshotStamp`, `geomHash?`
- Result: `{ ok: boolean, msg: string }`
- Errors: invalid mesh payload, geometry creation failure, IPC errors
- Example
```json
{ "jsonrpc":"2.0", "id": 1, "method":"rhino_import_snapshot", "params": { "uniqueId":"AI-TEST-0001", "units":"feet", "vertices":[[0,0,0],[1,0,0],[0,1,0]], "submeshes":[{"materialKey":"default","intIndices":[0,1,2]}], "snapshotStamp":"2025-10-05T00:00:00Z" } }
```

### rhino_get_selection
- Purpose: Return RevitUniqueIds of selected Rhino instances and preview delta transforms
- Direction: Server → Plugin (IPC)
- Params: `{}`
- Result:
```json
{ "ok": true, "items": [
  {
    "uniqueId":"...",
    "delta":{ "translate": {"x": number, "y": number, "z": number, "units":"feet"}, "rotateZDeg": number },
    "guard":{ "snapshotStamp":"...", "geomHash":"..." }
  }
]}
```
- Errors: none on empty selection (returns `items: []`)

### rhino_commit_transform
- Purpose: Compute and commit delta (T/yaw) of current selection to Revit MCP (`apply_transform_delta`)
- Direction: Server → Plugin (IPC) → Revit MCP (HTTP)
- Params: `{}`
- Result: `{ ok: boolean, appliedCount: number, errors: number }`
- Notes: Selection filtered to objects with `RevitRefUserData`; scale/shear deltas rejected

### rhino_lock_objects / rhino_unlock_objects
- Purpose: Lock/unlock targeted Rhino objects (prevent edit)
- Direction: Server → Plugin (IPC)
- Params: `{ uniqueIds?: string[] }` (if omitted, uses current selection)
- Result: `{ ok: true, count: number }` (affected objects)

### rhino_refresh_from_revit (stub)
- Purpose: Placeholder to trigger refresh from Revit MCP
- Direction: Server → Plugin (IPC) [future: Plugin ↔ Revit MCP]
- Params: `{}`
- Result: `{ ok: true, msg: string }`

### rhino_import_by_ids
- Purpose: Import Rhino objects by Revit UniqueIds
- Direction: Server → Revit MCP (`get_instance_geometry`) → Plugin IPC (`rhino_import_snapshot`)
- Params: `{ "uniqueIds": [string], "revitBaseUrl"?: string }`
- Result: `{ ok: boolean, imported: number, errors: number }`

## Examples

PowerShell (Invoke‑WebRequest)
```powershell
$base = 'http://127.0.0.1:5200'
$body = '{"jsonrpc":"2.0","id":1,"method":"rhino_get_selection","params":{}}'
Invoke-WebRequest -Method POST -Uri "$base/rpc" -Body $body -ContentType 'application/json; charset=utf-8'
```

curl
```bash
curl -s -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":2,"method":"rhino_import_by_ids","params":{"uniqueIds":["REVIT-UID"],"revitBaseUrl":"http://127.0.0.1:5210"}}' \
  http://127.0.0.1:5200/rpc
```

## Transform Semantics

- Baseline `B` is captured at import time and stored in `RevitRefUserData`
- Current `C` is the `InstanceObject.InstanceXform`
- Delta `Δ = inverse(B) * C`
- Validation: columns of `Δ` linear part must be ~unit length and orthogonal; `|det| ≈ 1`
- Translation: `(Δ.m03, Δ.m13, Δ.m23)` (mm) → convert to feet for Revit
- Rotation yaw: angle of rotated X-axis projected to XY plane

## Status and Stability Notes

- Server always responds HTTP 200 (JSON‑RPC `error` conveys failures)
- Server logs JSON lines to `%LOCALAPPDATA%/RhinoMCP/logs`
- Ensure no stale `dotnet` process is locking `bin` during rebuilds

