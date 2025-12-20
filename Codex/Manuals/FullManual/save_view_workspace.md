# save_view_workspace

- Category: UI / Views
- Purpose: Save the current **view workspace** (open views, active view, per-view zoom, per-view 3D camera) to an external JSON snapshot **bound to the current project** via the MCP Ledger `doc_key`.

## Overview
This command captures the current UI view workspace and writes it to:

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

`doc_key` is taken from the MCP Ledger `ProjectToken` stored in Revit `DataStorage`. This prevents restoring a snapshot into the wrong project.

Notes / limits:
- Window layout (docking/tab order/monitor placement) is **not** restorable via Revit API.
- Zoom/camera restore is best-effort; some view types do not support zoom corner APIs.

## Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| doc_key | string | no | current ledger `ProjectToken` |
| sink | string | no | `file` |
| include_zoom | bool | no | settings (`viewWorkspace.includeZoom`) |
| include_3d_orientation | bool | no | settings (`viewWorkspace.include3dOrientation`) |
| retention | int | no | settings (`viewWorkspace.retention`) |

## Result (high level)
- `doc_key`: ledger-bound key used for file naming
- `savedPath`: snapshot path
- `openViewCount`: number of captured open UI views
- `activeViewUniqueId`: active view unique id at capture time
- `warnings`: best-effort warnings (optional)

## Related
- [restore_view_workspace](restore_view_workspace.md)
- [set_view_workspace_autosave](set_view_workspace_autosave.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)

