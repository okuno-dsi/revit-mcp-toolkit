# describe_command

- Category: MetaOps
- Purpose: Describe a command (metadata lookup).

## Overview
Returns command metadata from the add-in’s runtime registry (category/kind/importance/risk/tags/aliases, etc.)
plus agent-friendly hints:
- `paramsSchema` / `resultSchema` (JSON Schema, currently a permissive fallback)
- `exampleJsonRpc`
- `commonErrorCodes`
 - (optional) `terminology` when `term_map_ja.json` is available (synonyms / negative_terms / sources)

- Alias: `help.describe_command`
- Step 4: `data.name` is the **canonical domain-first name**; legacy names (e.g., `get_project_info`) resolve to the same entry and appear in `data.aliases`.

## Usage
- Method: `describe_command`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| method | string | yes |  |

Notes:
- `name` or `command` can be used instead of `method` (compat).

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.describe_command",
  "params": { "method": "element.get_walls" }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Command description",
  "data": {
    "name": "element.get_walls",
    "category": "ElementOps/Wall",
    "kind": "read",
    "importance": "normal",
    "risk": "low",
    "tags": ["ElementOps", "Wall"],
    "aliases": ["get_walls"],
    "paramsSchema": { "type": "object", "additionalProperties": true },
    "resultSchema": { "type": "object", "additionalProperties": true },
    "exampleJsonRpc": "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.get_walls\", \"params\":{} }",
    "commonErrorCodes": [
      { "code": "INVALID_PARAMS", "msg": "Missing/invalid parameters" },
      { "code": "UNKNOWN_COMMAND", "msg": "No such command" }
    ],
    "terminology": {
      "term_map_version": "xxxxxxxx",
      "synonyms": ["断面", "セクション"],
      "negative_terms": ["平断面"],
      "sources": ["view:SECTION_VERTICAL"]
    }
  }
}
```
