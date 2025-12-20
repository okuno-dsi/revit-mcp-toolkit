# get_view_workspace_restore_status

- Category: UI / Views
- Purpose: Return the current status of an in-progress `restore_view_workspace` operation.

## Overview
`restore_view_workspace` runs stepwise via Idling. This command is a lightweight way to check:
- whether restore is still running
- how many views were processed
- warnings/errors (if any)

## Parameters
None.

## Result (high level)
- `active`: whether a restore session is running
- `done`: whether it finished
- `sessionId`
- `totalViews`, `index`, `phase`
- `openedOrActivated`, `appliedZoom`, `applied3d`, `missingViews`
- `warnings`, `error`

## Related
- [restore_view_workspace](restore_view_workspace.md)

