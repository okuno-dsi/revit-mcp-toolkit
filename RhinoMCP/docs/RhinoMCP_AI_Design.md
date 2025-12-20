# RhinoMCP System Design (For AI Agent Integration)

## 1. Objective

Enable **3D position and rotation adjustments** of Revit instances through Rhino,  
without modifying geometry.  
Rhino performs move/rotate operations → sends ΔTransform back to Revit.

### Revit Side (already implemented)

- `get_instance_geometry` → get element mesh (GLTF/OBJ-compatible)
- `export_view_mesh` → export visible geometry in current view
- `apply_transform_delta` → apply translation + rotation delta to element

### Rhino Side

- Rhino Plug-in (.NET Framework 4.8, Rhino 7/8 compatible)
- Starts/stops **RhinoMcpServer.exe** (external process)
- Handles mesh import, UserData storage, ΔTransform extraction, Revit sync

### Rules

- **Scaling and Shear forbidden**
- **Translation + Rotation only**
- If Δ contains scale/shear → reject & warn user

---

## 2. System Architecture

```
[AI Agent / Gemini CLI / Codex CLI]
         │ JSON-RPC over HTTP
         ▼
 ┌──────────────────────┐                 ┌──────────────────────┐
 │  RhinoMcpServer.exe  │◀──control──────▶│  Rhino Plug-in       │
 │  (.NET 6+, out-proc) │                 │  (.NET 4.8, in-proc) │
 └──────────────────────┘                 └──────────────────────┘
          ▲                                         │
          │                                         │ RhinoCommon API
          │                                         ▼
          │                                 [Rhino Document]
          │
          │ HTTP (JSON-RPC)
          ▼
 ┌──────────────────────┐
 │  RevitMcpServer.exe  │   ← existing (.NET 6+)
 └──────────────────────┘
```

### Key Principle

- **Rhino plug-in** stays on .NET 4.8 (due to RhinoCommon constraints)  
- **RhinoMcpServer** uses .NET 6+ for modern dependencies  
- **RevitMcpServer** remains the existing external MCP endpoint  
- Communication via **HTTP JSON-RPC**, no in-process bridging required

---

## 3. Project Layout

```
RhinoMCP/
├─ RhinoMcpPlugin/
│  ├─ Commands/
│  │  ├─ McpImportCommand.cs
│  │  ├─ McpCommitTransformCommand.cs
│  │  ├─ McpLockObjectsCommand.cs
│  │  └─ McpSettingsCommand.cs
│  ├─ Core/
│  │  ├─ RevitLinkMeta.cs
│  │  ├─ UserData/RevitRefUserData.cs
│  │  ├─ TransformUtil.cs
│  │  ├─ UnitUtil.cs
│  │  ├─ RhinoDocStore.cs
│  │  ├─ Logger.cs
│  │  └─ HttpJsonRpcClient.cs
│  ├─ UI/
│  │  ├─ Toolbar.rui
│  │  └─ Panels/SettingsPanel.cs
│  └─ RhinoMcpPlugin.csproj
│
├─ RhinoMcpServer/
│  ├─ Program.cs
│  ├─ Rpc/
│  │  ├─ RpcRouter.cs
│  │  ├─ Rhino/
│  │  │  ├─ ImportSnapshotCommand.cs
│  │  │  ├─ GetSelectionCommand.cs
│  │  │  ├─ CommitDeltaCommand.cs
│  │  │  ├─ LockObjectsCommand.cs
│  │  │  └─ RefreshFromRevitCommand.cs
│  │  └─ RevitProxy/
│  │     └─ RevitMcpClient.cs
│  ├─ Models/
│  │  ├─ MeshPacket.cs
│  │  ├─ DeltaTransform.cs
│  │  └─ RpcBase.cs
│  ├─ wwwroot/
│  │  ├─ openapi.json
│  │  └─ ai-plugin.json
│  └─ RhinoMcpServer.csproj
│
└─ docs/
   └─ DESIGN.md
```

---

## 4. Data Model

### 4.1 Revit → Rhino (Import)

Input:  
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

Processing in Rhino:
- Convert to Mesh → create **InstanceDefinition (Block)**
- Attach **RevitRefUserData**
- Register to **RhinoDocStore**

Stored metadata:
```
RevitUniqueId, BaselineWorldXform, Units, ScaleToRhino, SnapshotStamp, GeomHash
```

### 4.2 Rhino → Revit (Commit ΔTransform)

Algorithm:
```
B = BaselineWorldXform
C = CurrentWorldXform
Δ = inverse(B) * C
```

Decompose:
- Extract T (translation), R (rotation)
- Reject if Scale/Shear != Identity

Payload example:
```json
{
  "uniqueId":"...",
  "delta":{
    "translate":{"x":1.23,"y":0.0,"z":0.0,"units":"feet"},
    "rotateZDeg":15.0
  },
  "guard":{"snapshotStamp":"...","geomHash":"..."}
}
```

---

## 5. MCP Commands (RhinoMcpServer)

| Method | Purpose | Notes |
|--------|----------|-------|
| `rhino_import_snapshot` | Import Revit mesh snapshot | creates blocks + UserData |
| `rhino_get_selection` | Return selected UniqueIds and Δ preview | |
| `rhino_commit_transform` | Compute Δ and send to Revit | |
| `rhino_lock_objects` / `rhino_unlock_objects` | Lock/unlock geometry editing | |
| `rhino_refresh_from_revit` | Fetch latest geometry from Revit | |

All responses:
```json
{ "ok": true/false, "msg": "..." }
```
Unknown methods return `-32601`.

---

## 6. Units and Coordinate Systems

| System | Units | Conversion |
|---------|--------|------------|
| Rhino | mm | 1 ft = 304.8 mm |
| Revit | feet | mm → ft conversion before POST |

- Coordinate base: Revit **Internal Origin**
- Rotation axis: global Z (default)
- Local-axis rotation (e.g. for beams) → future extension

---

## 7. Scale / Shear Detection

Algorithm:
```
T = translation(Δ)
A = linearPart(Δ)
A = R * S  // polar decomposition
if S ≠ I or shear ≠ 0 → reject
```

Implementation (RhinoCommon):
```csharp
Transform.DecomposeAffine()
TransformSimilarityType
```
Reject all transforms not of type `Similarity`.

---

## 8. Rhino Plug-in UI Design

Toolbar Commands:
```
Import from Revit
Lock / Unlock Shapes
Commit Transform
Refresh from Revit
Settings
```

Workflow:
```
1. Import from Revit
2. (Optional) Lock Shapes
3. Move/Rotate using Gumball
4. Commit Transform
```

Error messages:
```
Scaling/Shear is not allowed.
Model changed in Revit; please refresh.
```

---

## 9. Logging and Fault Tolerance

- Logs: `%LOCALAPPDATA%\RhinoMCP\logs\`
  - `RhinoMcpPlugin.log`
  - `RhinoMcpServer.log`
- Overwrite on startup
- JSON line format per request: `{time, id, method, ok, msg}`
- HTTP always returns 200; app errors use `ok:false`

Error codes:
```
NO_LOCATION
SCALE_SHEAR_FORBIDDEN
STALE_SNAPSHOT
```

---

## 10. Build and Installation

### Requirements
- Visual Studio 2022
- Rhino 7 or 8
- .NET Framework 4.8 Developer Pack
- .NET 6 SDK

### Steps
**RhinoMcpPlugin**
- Create Rhino Plug-in (C#)
- Reference `RhinoCommon.dll`
- Fixed PlugInId
- Build → `.rhp` output

**RhinoMcpServer**
- `dotnet new web`
- Implement `/rpc` (JSON-RPC)
- `dotnet publish -c Release -r win-x64`

Default ports:
```
Revit MCP: 5210
Rhino MCP: 5215+ (auto increment)
Lock path: %LOCALAPPDATA%\RhinoMCP\locks\{port}.lock
```

---

## 11. Testing Plan

| Type | Description |
|------|--------------|
| Unit | Δ decomposition, unit conversion, UserData persistence |
| Integration | Revit→Rhino→Revit sync test |
| Load | 10,000 mesh import performance, 100 batch commits |
| Safety | Verify scale/shear rejection, stale model handling |

---

## 12. Security & Reliability

- Bind HTTP to `127.0.0.1` only
- No external access / no CORS
- Port lock mechanism
- Cleanup orphan locks on startup
- Crash-safe

---

## 13. Summary (for AI Agents)

- Goal: ΔTransform sync between Revit and Rhino  
- Translation + Rotation only  
- Geometry frozen  
- Units consistent (mm ↔ ft)  
- MCP JSON-RPC endpoints  
- Rhino in-proc, server out-proc  
- Multi-agent ready (Codex, Gemini)

---

## 14. Future Extensions

- Local-axis rotation (`rotate.axis`)
- Rhino ↔ Blender bridge
- AI-driven layout adjustments
- Expanded analytics error codes
- Unified OpenAPI generation

---

**End of Document**
