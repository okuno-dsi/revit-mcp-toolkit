# search_commands

- Category: MetaOps
- Purpose: Search available commands (ranked).

## Overview
Searches available command method names using the add-in’s runtime command metadata registry.

- Alias: `help.search_commands`
- Step 4: results use **canonical domain-first names** (e.g., `doc.get_project_info`); legacy names remain callable and appear in `aliases`.

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
