# search_commands

- Category: MetaOps
- Purpose: Search available commands (ranked).

## Overview
Searches available command method names using the add-in’s runtime command metadata registry.

- Alias: `help.search_commands`
- Step 4: results use **canonical domain-first names** (e.g., `doc.get_project_info`); legacy names remain callable and appear in `aliases`.

## Terminology-Aware Search (term_map_ja.json)
If `term_map_ja.json` is available, `search_commands` boosts results using Japanese synonyms and disambiguation rules.

Typical examples:
- `断面` / `セクション` ⇒ `create_section` (vertical section / 立断面)
- `平断面` / `平面図` / `伏図` ⇒ `create_view_plan` (plan)
- `立面` ⇒ `create_elevation_view`
- `RCP` / `天井伏図` ⇒ `create_view_plan` with `suggestedParams` hints (e.g. `view_family=CeilingPlan`) if supported

### Term Map File Locations
The add-in searches for `term_map_ja.json` in this order (first match wins):
- `%LOCALAPPDATA%\RevitMCP\term_map_ja.json`
- `%USERPROFILE%\Documents\Codex\Design\term_map_ja.json`
- `<AddinFolder>\Resources\term_map_ja.json`
- `<AddinFolder>\term_map_ja.json`
- Or set the env var `REVITMCP_TERM_MAP_JA_PATH`

### Extra Fields in Results
When a term map match is used, each `data.items[]` entry may include:
- `termScore` / `matched` / `hint` / `suggestedParams`

Also, `data.termMap` includes `term_map_version` plus compact default/disambiguation summaries for agents.

## Usage
- Method: `search_commands`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| query | string | no* |  |
| tags | string[] | no* |  |
| riskMax | string | no |  |
| limit | integer | no | 10 |
| category | string | no |  |
| kind | string | no |  |
| importance | string | no |  |
| prefixOnly | boolean | no | false |
| q | string | no (compat) |  |
| top | integer | no (compat) |  |

Notes:
- `query` and `tags` are Step 3 spec inputs. At least one of them is required.
- `q`/`top` are backward compatible aliases for `query`/`limit`.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.search_commands",
  "params": {
    "query": "place view on sheet",
    "tags": ["sheet", "place"],
    "limit": 10,
    "riskMax": "medium"
  }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Top matches",
  "data": {
    "items": [
      { "name": "sheet.place_view_auto", "score": 0.93, "summary": "Place a view; auto-duplicate if needed", "risk": "medium", "tags": ["sheet","place","auto"] }
    ]
  }
}
```

### Smoke Test Script
- `Manuals/Scripts/test_terminology_routing.ps1 -Port 5210`

## Related
- start_command_logging
- stop_command_logging
- agent_bootstrap

### Params Schema
```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string" },
    "tags": { "type": "array", "items": { "type": "string" } },
    "riskMax": { "type": "string", "enum": ["low", "medium", "high"] },
    "limit": { "type": "integer" },
    "category": { "type": "string" },
    "kind": { "type": "string", "enum": ["read", "write"] },
    "importance": { "type": "string", "enum": ["low", "normal", "high"] },
    "prefixOnly": { "type": "boolean" },
    "q": { "type": "string" },
    "top": { "type": "integer" }
  },
  "additionalProperties": true
}
```

### Result Schema
```json
{
  "type": "object",
  "properties": {
    "ok": { "type": "boolean" },
    "code": { "type": "string" },
    "msg": { "type": "string" },
    "data": {
      "type": "object",
      "properties": {
        "items": { "type": "array", "items": { "type": "object", "additionalProperties": true } }
      },
      "additionalProperties": true
    }
  },
  "additionalProperties": true
}
```
