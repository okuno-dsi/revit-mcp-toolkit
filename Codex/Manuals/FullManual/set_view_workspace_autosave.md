# set_view_workspace_autosave

- Category: UI / Views
- Purpose: Configure **autosave** for view workspace snapshots (crash resilience) and optionally toggle auto-restore.

## Overview
When autosave is enabled, the add-in periodically captures the active document’s view workspace and writes:

- `%APPDATA%\\RevitMCP\\ViewWorkspace\\workspace_{doc_key}.json`

Additionally:
- A snapshot is also saved on `DocumentClosing` / add-in `OnShutdown` (best-effort).

Settings are persisted in:
- `%LOCALAPPDATA%\\RevitMCP\\settings.json` → `viewWorkspace`

Defaults:
- `viewWorkspace.autosaveEnabled` is `true` (every 5 minutes, retention 10) unless you change it.

Revit UI:
- Ribbon tab `RevitMCPServer` → panel `Workspace`
  - `Autosave Toggle`: toggles `viewWorkspace.autosaveEnabled` (keeps current interval/retention)
  - `Reset Defaults`: resets `viewWorkspace` settings to defaults (`autoRestoreEnabled=true`, `autosaveEnabled=true`, `autosaveIntervalMinutes=5`, `retention=10`, `includeZoom=true`, `include3dOrientation=true`)

## Parameters
| Name | Type | Required | Default |
|---|---:|:---:|---|
| enabled | bool | yes |  |
| interval_minutes | int | no | 5 |
| retention | int | no | 10 |
| auto_restore_enabled | bool | no | unchanged |

Notes:
- `retention > 1` keeps an archive ring buffer (`workspace_{doc_key}_YYYYMMDD_HHMMSS.json`) in addition to `workspace_{doc_key}.json`.

## Related
- [save_view_workspace](save_view_workspace.md)
- [restore_view_workspace](restore_view_workspace.md)
- [get_view_workspace_restore_status](get_view_workspace_restore_status.md)
