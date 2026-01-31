# get_rebar_hosts_and_params

- Category: Rebar
- Purpose: Collect host information and selected parameters for multiple Rebar elements.

## Overview
Use this command to fetch **host IDs**, **type info**, and selected **parameter values** for Rebar-like elements (Rebar / RebarInSystem / AreaReinforcement / PathReinforcement).

## Usage
- Method: get_rebar_hosts_and_params

### Parameters
```jsonc
{
  "elementIds": [5785112, 5785113, 5785114],
  "includeHost": true,
  "includeParameters": ["モデル鉄筋径", "鉄筋番号", "ホスト カテゴリ"],
  "includeTypeInfo": true,
  "includeErrors": true
}
```

- `elementIds` (int[], required)
- `includeHost` (bool, optional; default: true)
- `includeParameters` (string[], optional; default: ["モデル鉄筋径","鉄筋番号","ホスト カテゴリ"])
- `includeTypeInfo` (bool, optional; default: true)
- `includeErrors` (bool, optional; default: true)

### Result (example)
```jsonc
{
  "ok": true,
  "count": 2,
  "items": [
    {
      "elementId": 5785112,
      "uniqueId": "...",
      "typeId": 4857541,
      "typeName": "D25",
      "familyName": "鉄筋棒",
      "hostId": 123456,
      "hostCategory": "構造柱",
      "parameters": {
        "モデル鉄筋径": "25 mm",
        "鉄筋番号": "1",
        "ホスト カテゴリ": "構造柱"
      }
    }
  ],
  "errors": [
    { "elementId": 5785114, "code": "NOT_FOUND", "msg": "Element not found." }
  ]
}
```

## Notes
- If a parameter is not found on the instance, the command tries the **type** parameter of that element.
- `includeErrors=true` collects per-element failures (e.g., NOT_REBAR / NOT_FOUND).

