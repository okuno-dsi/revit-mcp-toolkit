# element.query_elements

- Category: ElementOps/Query
- Purpose: Query elements using structured filters (categories, classNames, levelId, name, bbox, parameters).

## Overview
Use this when you need more reliable targeting than a keyword search.

Typical workflow:
1) `element.query_elements` → get candidate IDs
2) optional validation with read commands (parameters, type, etc.)
3) run write commands on the resulting IDs

## Usage
- Method: `element.query_elements`

### Parameters
Top-level structure:
```jsonc
{
  "scope": { "viewId": 0, "includeHiddenInView": false },
  "filters": {
    "categories": [],
    "classNames": [],
    "levelId": 0,
    "name": { "mode": "contains", "value": "", "caseSensitive": false },
    "bbox": {
      "min": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
      "max": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
      "mode": "intersects"
    },
    "parameters": [
      {
        "param": { "kind": "name", "value": "Mark" },
        "op": "contains",
        "value": "A-",
        "caseSensitive": false
      }
    ]
  },
  "options": {
    "includeElementType": true,
    "includeBoundingBox": false,
    "includeParameters": [],
    "maxResults": 200,
    "orderBy": "id"
  }
}
```

#### `scope`
| Name | Type | Required | Default |
|---|---|---|---|
| viewId | int | no | null |
| includeHiddenInView | bool | no | false |

#### `filters`
| Name | Type | Required | Notes |
|---|---|---|---|
| categories | string[] | no | Same rules as `element.search_elements` |
| classNames | string[] | no | Example: `"Wall"`, `"Floor"` (best-effort) |
| levelId | int | no | Best-effort resolution (varies by element kind) |
| name | object | no | `mode: equals/contains/startsWith/endsWith/regex` |
| bbox | object | no | `unit: mm/cm/m/ft/in`, `mode: intersects/inside` |
| parameters | object[] | no | Post-filter (first version), see below |

#### `filters.parameters[]`
- `param.kind`: `name` \| `builtin` \| `guid` \| `paramId`
- `op`:
  - For string parameters: `equals/contains/startsWith/endsWith`
  - For int/ElementId: `equals/gt/gte/lt/lte/range`
  - For double: `equals/gt/gte/lt/lte/range` (**length-only conversion**, first version)
- For `range`, specify `min` and `max`.
- For double comparisons you can pass `unit` (mm/cm/m/ft/in). (Internal conversion assumes length.)

#### `options`
| Name | Type | Required | Default |
|---|---|---|---|
| includeElementType | bool | no | true |
| includeBoundingBox | bool | no | false |
| includeParameters | string[] | no | [] |
| maxResults | int | no | 200 (hard limit: 2000) |
| orderBy | string | no | "id" ("id" or "name") |

### Example Request (bbox + parameter)
```json
{
  "jsonrpc": "2.0",
  "id": "walls-A",
  "method": "element.query_elements",
  "params": {
    "scope": { "viewId": 12345, "includeHiddenInView": false },
    "filters": {
      "categories": ["Walls"],
      "levelId": 67890,
      "name": { "mode": "contains", "value": "Basic Wall", "caseSensitive": false },
      "bbox": {
        "min": { "x": 0, "y": 0, "z": 0, "unit": "mm" },
        "max": { "x": 10000, "y": 10000, "z": 3000, "unit": "mm" },
        "mode": "intersects"
      },
      "parameters": [
        { "param": { "kind": "name", "value": "Mark" }, "op": "contains", "value": "A-" }
      ]
    },
    "options": { "includeElementType": true, "includeBoundingBox": false, "maxResults": 200, "orderBy": "id" }
  }
}
```

## Result
- `ok`: boolean
- `elements[]`: element summaries (`id/uniqueId/name/category/className/levelId` + optional `typeId`/`bbox`/`parameters`)
- `diagnostics`:
  - `appliedFilters[]`
  - `counts`: `{ initial, afterQuickFilters, afterSlowFilters, returned }`
  - `warnings[]` (if any)

## Notes
- `filters.levelId` is **best-effort**: not every element kind stores level in the same way.
- `filters.parameters[]` is a post-filter in this first version; use `scope`/`categories`/`bbox` to narrow candidates for performance.
- Double parameter comparisons currently assume “length” conversion; avoid using this for non-length doubles (e.g. thermal properties).

