# help_suggest

- Category: MetaOps
- Purpose: Deterministic “what should I run?” suggestions (recipes + commands).

## Overview
`help.suggest` returns ranked suggestions for a Japanese query, using:
- the current Revit context (selection / active view),
- the add-in’s command registry (`CommandMetadataRegistry`),
- the Japanese glossary (`glossary_ja.json`).

This command **never executes** model changes. It only returns suggestions.

## Usage
- Method: `help.suggest`

### Parameters
| Name | Type | Required | Default |
|---|---|---:|---|
| queryJa | string | yes* |  |
| query | string | no |  |
| q | string | no |  |
| limit | integer | no | 5 |
| safeMode | boolean | no | true |
| includeContext | boolean | no | false |

Notes:
- Use `queryJa` (preferred). `query` / `q` are accepted for convenience.
- `safeMode=true` down-ranks write-capable commands unless the query clearly implies write intent.

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "help.suggest",
  "params": {
    "queryJa": "部屋にW5を内張り。柱も拾って。既存はスキップ。",
    "limit": 5,
    "safeMode": true,
    "includeContext": true
  }
}
```

### Example Result (shape)
```jsonc
{
  "ok": true,
  "code": "OK",
  "msg": "Suggestions",
  "data": {
    "normalized": { "actions": [], "entities": [], "concepts": [], "paramHints": {}, "unknownTerms": [] },
    "suggestions": [
      { "kind": "recipe", "id": "finish_wall_overlay_room_w5_v1", "method": "room.apply_finish_wall_type_on_room_boundary", "confidence": 0.86 }
    ],
    "didYouMean": [],
    "glossary": { "ok": true, "code": "OK", "path": "..." }
  }
}
```

## Glossary File Locations
The add-in searches for `glossary_ja.json` in this order (first match wins):
- `%LOCALAPPDATA%\\RevitMCP\\glossary_ja.json`
- `%USERPROFILE%\\Documents\\Codex\\Design\\glossary_ja.json`
- `<AddinFolder>\\Resources\\glossary_ja.json`
- `<AddinFolder>\\glossary_ja.json`
- Or set the env var `REVITMCP_GLOSSARY_JA_PATH`

If `glossary_ja.json` is not found, the add-in also tries `glossary_ja.seed.json` in the same locations (best-effort).

## Related
- search_commands (`help.search_commands`)
- describe_command (`help.describe_command`)
- get_context

