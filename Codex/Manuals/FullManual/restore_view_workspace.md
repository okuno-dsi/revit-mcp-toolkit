# restore_view_workspace

- Category: UI / Views
- Purpose: Restore a previously saved **view workspace** snapshot for the currently open Revit project (open views, active view, zoom/camera).

## Overview
The snapshot is loaded from the external file store (default):

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

Safety:
- The command resolves the current `doc_key` from the MCP Ledger (`ProjectToken` in `DataStorage`).
- Restore proceeds only when snapshot `doc_key` matches the current document `doc_key`.
- If `doc_path_hint` differs from the current document path, it returns a warning (restore still proceeds because `doc_key` matched).

Execution model:
- Restore runs **asynchronously via Idling** (stepwise: activate view → apply zoom/camera → next view).
- Use `get_view_workspace_restore_status` to check progress.
 - If a view no longer exists (deleted/renamed), it is skipped and counted in `missingViews` (check `warnings` as well).

Auto behavior:
- Auto-restore is **enabled by default** when you open a project (can be disabled via `%LOCALAPPDATA%\\RevitMCP\\settings.json` → `viewWorkspace.autoRestoreEnabled`).

## Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| doc_key | string | no | current ledger `ProjectToken` |
| source | string | no | `file` (`auto` is accepted and treated as file) |
| include_zoom | bool | no | settings (`viewWorkspace.includeZoom`) |
| include_3d_orientation | bool | no | settings (`viewWorkspace.include3dOrientation`) |
| activate_saved_active_view | bool | no | true |

## Result (high level)
- `msg`: accepted message (restore continues in background)
- `restoreSessionId`: id to query via `get_view_workspace_restore_status`
- `openViews`: views scheduled for restore
- `warnings`: doc path hint mismatch etc (optional)

## Related
- [save_view_workspace](save_view_workspace.md)
- [set_view_workspace_autosave](set_view_workspace_autosave.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)
