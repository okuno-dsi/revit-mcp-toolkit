# element.search_elements

- Category: ElementOps/Query
- Purpose: Find elements by a forgiving keyword search (for discovery of ElementIds).

## Overview
Use this when you **don’t know ElementIds yet**.

- Searches across element **Name / Category / UniqueId / Type name**.
- Optional scoping by `categories`, `viewId`, `levelId`.
- Returns lightweight `elements[]` summaries suitable for the next step (e.g., pass IDs to move/update/override commands).

## Usage
- Method: `element.search_elements`

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| keyword | string | yes |  |
| categories | string[] | no | [] (all categories) |
| viewId | int | no | null |
| levelId | int | no | null |
| includeTypes | bool | no | false |
| caseSensitive | bool | no | false |
| maxResults | int | no | 50 (hard limit: 500) |

Notes:
- `categories[]` accepts friendly names (EN/JA) and enum names:
  - `"Walls"`, `"Floors"`, `"Doors"`, `"Windows"`, `"Rooms"`, `"Spaces"`, `"Areas"`
  - `"壁"`, `"床"`, `"建具"`, `"窓"`, `"部屋"`, `"スペース"`, `"エリア"`
  - `"OST_Walls"` (BuiltInCategory enum name)

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": "find-doors",
  "method": "element.search_elements",
  "params": {
    "keyword": "Single",
    "categories": ["Doors"],
    "viewId": 12345,
    "maxResults": 50
  }
}
```

## Result
- `ok`: boolean
- `elements[]`: element summaries: `{ id, uniqueId, name, category, className, levelId, typeId }`
- `counts`: `{ candidates, returned }`
- `warnings[]`: present when category names were partially unsupported

## Notes
- If you omit both `categories` and `viewId`, this may scan many elements; prefer scoping when possible.
- If you provide `categories` but none are recognized, the command returns `ok:false` (`code=INVALID_CATEGORY`).

