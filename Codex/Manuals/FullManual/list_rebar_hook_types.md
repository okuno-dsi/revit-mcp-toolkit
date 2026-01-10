# list_rebar_hook_types

- Category: Rebar
- Purpose: List `RebarHookType` definitions (hook types) available in the current document.

## Usage
- Method: `list_rebar_hook_types`

### Parameters
| Name | Type | Required | Default | Notes |
|---|---|---:|---:|---|
| includeCountByAngle | bool | no | true | Adds `countByAngleDeg` summary (rounded integer degrees). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_rebar_hook_types",
  "params": { "includeCountByAngle": true }
}
```

## Notes
- Hook names and availability are **project/template dependent**.
- Some hook types are only compatible with specific `RebarStyle` (e.g. ties/stirrups vs standard bars). When creating ties/stirrups, prefer hook types that are compatible with `RebarStyle.StirrupTie`.

