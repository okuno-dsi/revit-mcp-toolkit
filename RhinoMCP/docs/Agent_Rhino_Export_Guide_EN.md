## Revit → Rhino Export & Import Guide (Agent‑Ready)

This guide explains how an automation agent (or operator) can export Revit geometry to Rhino `.3dm` and import it into Rhino reliably, using the Revit MCP add‑in (port 5210), RhinoMcpServer (port 5200), and RhinoMcpPlugin (port 5201).

### Components & Ports

- Revit MCP Add‑in (HTTP) — `http://127.0.0.1:5210`
- RhinoMcpServer (HTTP) — `http://127.0.0.1:5200`
- RhinoMcpPlugin (Rhino IPC) — `http://127.0.0.1:5201`

Make sure:
- Revit is running with the MCP add‑in listening on port 5210.
- Rhino is running and the RhinoMcpPlugin.rhp is loaded (5201 responds).
- RhinoMcpServer is started (5200/healthz returns ok).

### Units Policy (SI‑first)

- Revit internal units are feet; this guide targets SI (millimeters) by default.
- Revit export commands now resolve units via UnitHelper/UnitSettings:
  - If `unitsOut` is specified, it is honored (e.g. `"mm"` or `"feet"`).
  - Otherwise, `UnitHelper.ResolveUnitsMode(doc, params)` is used: `SI` → mm, `Project` → Revit project display units, `Raw` → feet.
- Rhino document should be set to millimeters for consistent import (File → Properties → Units → Model units = Millimeters).

### Quick Start — Export Selected Elements (SI, mm)

Prerequisites:
- Select target elements in Revit.
- Ensure an active 3D view is current.

1) Mesh export (`export_view_3dm`)

- Request (PowerShell via Revit MCP queue):

```
$port = 5210
$base = "http://127.0.0.1:$port"
$out  = "C:/path/to/RevitSelection_Mesh_SI.3dm"  # change as needed
$view = @{ jsonrpc='2.0'; id=1; method='get_current_view'; params=@{} } | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri ($base+'/enqueue?force=1') -ContentType 'application/json' -Body $view | Out-Null
$v = Invoke-RestMethod -Method Get -Uri ($base+'/get_result')
$vid = $v.result.viewId

$sel = @{ jsonrpc='2.0'; id=2; method='get_selected_element_ids'; params=@{} } | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri ($base+'/enqueue?force=1') -ContentType 'application/json' -Body $sel | Out-Null
$s = Invoke-RestMethod -Method Get -Uri ($base+'/get_result')

$args = @{ viewId = [int]$vid; outPath = $out; elementIds = @($s.result.elementIds); includeLinked=$true; unitsOut='mm' }
$call = @{ jsonrpc='2.0'; id=3; method='export_view_3dm'; params=$args } | ConvertTo-Json -Depth 8
Invoke-RestMethod -Method Post -Uri ($base+'/enqueue?force=1') -ContentType 'application/json' -Body $call | Out-Null
$r = Invoke-RestMethod -Method Get -Uri ($base+'/get_result')
```

2) Brep‑first export (`export_view_3dm_brep`)

- Same as above, but `method` is `export_view_3dm_brep` and `outPath` differs:

```
$args = @{ viewId = [int]$vid; outPath = "C:/path/to/RevitSelection_Brep_SI.3dm"; elementIds = @($s.result.elementIds); includeLinked=$true; unitsOut='mm' }
$call = @{ jsonrpc='2.0'; id=4; method='export_view_3dm_brep'; params=$args } | ConvertTo-Json -Depth 8
Invoke-RestMethod -Method Post -Uri ($base+'/enqueue?force=1') -ContentType 'application/json' -Body $call | Out-Null
$r2 = Invoke-RestMethod -Method Get -Uri ($base+'/get_result')
```

Notes:
- For very large selections, chunk the `elementIds` into groups and export multiple `.3dm` parts. See “Merging 3DM files”.
- The `.3dm` is written in SI (mm), and the file header’s unit system is set accordingly.

### Import into Rhino

Option A — Manual (safe, recommended baseline):
- Rhino: File → Import → select the exported `.3dm`.
- If prompted, choose to keep file units and do not auto‑scale.

Option B — Automated via RhinoMcpServer:
- Requires RhinoMcpPlugin loaded (5201) and RhinoMcpServer (5200).
- Call `import_3dm` on the server:

```
POST http://127.0.0.1:5200/rpc
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "import_3dm",
  "params": { "path": "C:/path/to/RevitSelection_Brep_SI.3dm", "autoIndex": true, "units": "mm" }
}
```

If the plugin is not current or returns an error, fall back to manual import.

### Converting to Rhino 7 Format

If your workflow requires Rhino 7 `.3dm`:

```
POST http://127.0.0.1:5200/rpc
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "convert_3dm_version",
  "params": { "src": "C:/path/to/Model.3dm", "version": 7 }
}
```

### Merging 3DM Files

When exporting large selections in parts, merge them:

```
POST http://127.0.0.1:5200/rpc
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "merge_3dm_files",
  "params": {
    "inputs": [
      "C:/Exports/Part1.3dm",
      "C:/Exports/Part2.3dm"
    ],
    "dst": "C:/Exports/Merged_All.3dm",
    "version": 7
  }
}
```

### Troubleshooting

- No geometry or wrong scale in Rhino:
  - Ensure Rhino document units are Millimeters before import.
  - Verify Revit export used SI: `unitsOut="mm"` or UnitMode=SI.
- Elements look “split” in Brep:
  - Use the updated Brep export (polymesh stream + per‑element mesh merge → single Brep). For very detailed models, prefer mesh export for speed.
- Server/Plugin health:
  - `GET http://127.0.0.1:5200/healthz` → `{ ok: true }`
  - `POST http://127.0.0.1:5201/rpc` with `{method:"rhino_get_selection"}` should return 200.

### API Reference (summary)

- Revit MCP (5210)
  - `get_selected_element_ids` → `{ ok, elementIds: [] }`
  - `get_current_view` → `{ ok, viewId }`
  - `export_view_3dm` (mesh)
  - `export_view_3dm_brep` (brep‑first)
  - Common params: `{ viewId, outPath, elementIds?, includeLinked, unitsOut }`

- RhinoMcpServer (5200)
  - `import_3dm` → forwards to plugin to import `.3dm`
  - `convert_3dm_version` → write `.3dm` as version 7/8
  - `merge_3dm_files` → merge multiple `.3dm` into one
  - `list_revit_objects` / `find_by_element` / `collect_boxes` (optional utilities)

- RhinoMcpPlugin (5201)
  - `rhino_get_selection`, `rhino_import_snapshot` (mesh snapshots), `rhino_import_3dm`

### Paths in This Repository

- PowerShell helper (Brep selected → SI → import):
  - `scripts/export_selected_to_rhino_brep.ps1`
- Server utilities:
  - `import_3dm`, `convert_3dm_version`, `merge_3dm_files` (via `http://127.0.0.1:5200/rpc`)

### Best Practices

- Start with mesh export for quick validation; switch to Brep for detailed modeling.
- Keep Revit selection moderate; for large sets, export in parts and merge 3dm.
- Always verify units end‑to‑end (Revit → file header → Rhino document units).

