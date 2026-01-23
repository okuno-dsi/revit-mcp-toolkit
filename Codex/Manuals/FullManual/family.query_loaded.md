# family.query_loaded

- Category: Family
- Purpose: Query loaded families/types (loadable symbols, in-place families, optional system types) with fast filtering.
- NEW: 2026-01-22

## Overview
Returns a unified list of loaded family definitions without scanning instances. Use this to quickly discover which families/types are available in the current document.

## Usage
- Method: family.query_loaded

### Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| q | string | no | "" |
| category | string | no | "" |
| builtInCategory | string | no | "" |
| includeLoadableSymbols | bool | no | true |
| includeInPlaceFamilies | bool | no | true |
| includeSystemTypes | bool | no | false |
| systemTypeCategoryWhitelist | string[] | no |  |
| limit | int | no | 50 |
| offset | int | no | 0 |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "family.query_loaded",
  "params": {
    "q": "door",
    "category": "",
    "builtInCategory": "OST_Doors",
    "includeLoadableSymbols": true,
    "includeInPlaceFamilies": true,
    "includeSystemTypes": false,
    "systemTypeCategoryWhitelist": ["OST_Walls", "OST_Floors"],
    "limit": 50,
    "offset": 0
  }
}
```

### Result (shape)
```json
{
  "ok": true,
  "items": [
    {
      "kind": "LoadableSymbol",
      "category": "Doors",
      "builtInCategory": "OST_Doors",
      "familyId": 12345,
      "familyName": "Single-Flush",
      "typeId": 67890,
      "typeName": "0915 x 2134mm",
      "isInPlace": false,
      "score": 1.0
    }
  ],
  "totalApprox": 120
}
```

## Notes
- `q` is tokenized; all tokens must appear (partial matches accepted).
- `category` matches the display name; `builtInCategory` matches `BuiltInCategory` names (e.g. `OST_Doors`).
- System types are only returned when `includeSystemTypes=true`, and can be further filtered by `systemTypeCategoryWhitelist`.

## Related
- get_family_types
- get_family_instances
- summarize_family_types_by_category
