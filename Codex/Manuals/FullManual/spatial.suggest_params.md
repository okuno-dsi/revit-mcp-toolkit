# spatial.suggest_params

- Category: Spatial
- Purpose: Suggest parameter names for Room / Space / Area (fuzzy matching).

## Overview
This helper suggests parameter names when the user input is ambiguous. It samples Room/Space/Area elements and ranks candidates using fuzzy matching.

- Use it when users provide vague names like "天井高" or "面積" and you need the exact parameter name.
- It does not modify the model.
- By default it samples up to 30 elements per kind (configurable).

## Usage
- Method: spatial.suggest_params

### Parameters
| Name | Type | Required | Description |
|---|---|---|---|
| kind | string | no | `room` / `space` / `area`. |
| kinds | string[] | no | Multiple kinds to scan. |
| hint | string | no | Single hint string to match (e.g. "天井高"). |
| hints | string[] | no | Multiple hints. |
| matchMode | string | no | `exact` / `contains` / `fuzzy` (default: `fuzzy`). |
| maxMatchesPerHint | int | no | Max matches to return per hint (default: 5). |
| maxCandidates | int | no | Max candidates when `includeAllCandidates=true` (default: 200). |
| sampleLimitPerKind | int | no | Sample size per kind (default: 30). |
| includeAllCandidates | bool | no | Include the full candidate list (default: true if no hints). |
| elementIds | int[] | no | Limit sampling to specific IDs. |
| all | bool | no | Use all elements (if `elementIds` is empty, this is assumed). |

### Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "spatial.suggest_params",
  "params": {
    "kinds": ["room", "space"],
    "hints": ["天井高", "面積"],
    "matchMode": "fuzzy",
    "maxMatchesPerHint": 5,
    "sampleLimitPerKind": 30
  }
}
```

## Response
```jsonc
{
  "ok": true,
  "kinds": ["room", "space"],
  "hints": ["天井高", "面積"],
  "matchMode": "fuzzy",
  "maxMatchesPerHint": 5,
  "sampleLimitPerKind": 30,
  "items": [
    {
      "kind": "room",
      "elementSampleCount": 30,
      "totalCandidates": 128,
      "byHint": [
        {
          "hint": "天井高",
          "matches": [
            {
              "name": "天井高",
              "id": 123456,
              "storageType": "Double",
              "dataType": "autodesk.spec.aec:length-2.0.0",
              "count": 30,
              "score": 100
            }
          ]
        }
      ]
    }
  ]
}
```

## Notes
- Suggestions are based on a **sample**; project-specific parameters not present in the sample may be missing.
- After deciding the parameter name, use `get_spatial_params_bulk` (or a specific `get_*_params`) to read values.
