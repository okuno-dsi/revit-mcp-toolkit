# cleanup_revitmcp_cache

- Category: System
- Purpose: Clean stale cache files under `%LOCALAPPDATA%\\RevitMCP` (best-effort).

## What it cleans (default)
- Root files: `server_state_*.json`, `*.bak_*`, `*.tmp*` older than `retentionDays`
- Directories: `%LOCALAPPDATA%\\RevitMCP\\data\\*` (old snapshot folders)
- Queue: `%LOCALAPPDATA%\\RevitMCP\\queue\\p####` (stale per-port queues)
  - Excludes the current port and ports that look active (based on lock files in `%LOCALAPPDATA%\\RevitMCP\\locks`)

## What it does NOT delete (default)
- `%LOCALAPPDATA%\\RevitMCP\\settings.json`
- `%LOCALAPPDATA%\\RevitMCP\\config.json`
- `%LOCALAPPDATA%\\RevitMCP\\failure_whitelist.json`
- `%LOCALAPPDATA%\\RevitMCP\\RebarMapping.json` / `RebarBarClearanceTable.json` (treated as user override/cache)

## Auto-cleanup
The add-in also runs an automatic cleanup at Revit startup (same policy, `retentionDays=7`).

## Usage
- Method: `cleanup_revitmcp_cache`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| dryRun | bool | no | true | If true, only reports what would be deleted. |
| retentionDays | int | no | 7 | Anything older than this can be deleted. |
| maxDeletedPaths | int | no | 200 | Truncates `data.deletedPaths` for agent-friendliness. |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "cleanup_revitmcp_cache",
  "params": {
    "dryRun": true,
    "retentionDays": 7
  }
}
```

