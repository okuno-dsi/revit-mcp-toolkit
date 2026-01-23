# meta.resolve_category

- Category: MetaOps
- Purpose: Resolve ambiguous category text into a BuiltInCategory (OST_*) using alias dictionary.

## Overview
This helper resolves user text like "通り心" or "柱" into a stable category ID (e.g., `OST_Grids`).  
It is designed to be mojibake‑resilient and returns candidates if the result is ambiguous.

- Dictionary file: `category_alias_ja.json` (add-in folder, UTF-8 no BOM).
- Ambiguous cases always return `ok=false` with candidates.

## Usage
- Method: meta.resolve_category

### Parameters
| Name | Type | Required | Description |
|---|---|---|---|
| text | string | yes | Raw user text to resolve. |
| maxCandidates | int | no | Max candidates to return (default: 5). |
| context | object | no | Optional context for scoring boosts. |

`context` fields:
- `selectedCategoryIds`: string[] or int[]  
  - Strings should be `OST_*`.  
  - Integers will be converted to `BuiltInCategory` when possible.
- `disciplineHint`: string (e.g., `Structure`)
- `activeViewType`: string (optional)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "meta.resolve_category",
  "params": {
    "text": "通り心",
    "context": {
      "selectedCategoryIds": ["OST_Grids"],
      "disciplineHint": "Structure",
      "activeViewType": "FloorPlan"
    },
    "maxCandidates": 5
  }
}
```

## Response
```jsonc
{
  "ok": true,
  "msg": null,
  "normalizedText": "通り心",
  "recoveredFromMojibake": false,
  "resolved": {
    "id": "OST_Grids",
    "builtInId": -2000220,
    "labelJa": "通り芯（グリッド）",
    "score": 0.93,
    "reason": "alias exact match; context selectedCategoryIds"
  },
  "candidates": [
    { "id": "OST_Grids", "builtInId": -2000220, "labelJa": "通り芯（グリッド）", "score": 0.93 }
  ],
  "dictionary": {
    "ok": true,
    "path": "...\\RevitMCPAddin\\category_alias_ja.json",
    "version": "2026-01-20",
    "lang": "ja-JP",
    "schemaVersion": 1,
    "sha8": "8c2f5a1b"
  }
}
```

## Notes
- Use the resolved `id` (e.g., `OST_Grids`) as the canonical category identifier in requests.
- If `ok=false`, ask the user to choose from `candidates` before proceeding.
