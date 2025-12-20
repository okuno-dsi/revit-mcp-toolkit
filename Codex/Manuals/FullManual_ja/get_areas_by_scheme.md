# get_areas_by_scheme

- カテゴリ: Area
- 目的: 指定した AreaScheme に属する Area 一覧を取得し、レベル名で絞り込みつつ、任意のパラメータ値も合わせて取得します。

## 概要
このコマンドは JSON-RPC 経由で Revit MCP アドインに対して実行されます。`schemeId` または `schemeName` から AreaScheme を特定し、そのスキームに属する Area を返します（必要に応じてレベル名でフィルタ）。

## 使い方
- メソッド: get_areas_by_scheme

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| schemeId | int | はい\* |  |
| schemeName | string | はい\* |  |
| levelNames | string[] | いいえ |  |
| includeParameters | string[] | いいえ |  |

\* `schemeId` または `schemeName` のいずれかが必須です。両方指定された場合は `schemeId` が優先されます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_areas_by_scheme",
  "params": {
    "schemeName": "Rentable",
    "levelNames": ["Level 2"],
    "includeParameters": ["Department", "Comments"]
  }
}
```

### 結果例（成功）
```jsonc
{
  "ok": true,
  "scheme": {
    "id": 102,
    "name": "Rentable"
  },
  "areas": [
    {
      "id": 5001,
      "number": "AR-201",
      "name": "Tenant A",
      "levelName": "Level 2",
      "area": 123.45,
      "unit": "m2",
      "extraParams": {
        "Department": {
          "name": "Department",
          "value": "Sales",
          "display": "Sales"
        },
        "Comments": {
          "name": "Comments",
          "value": "Key tenant",
          "display": "Key tenant"
        }
      }
    }
  ],
  "messages": [
    "AreaScheme 'Rentable' (id=102) resolved.",
    "1 Areas returned for requested levels."
  ]
}
```

一致する Area が 1 件も無い場合でも `ok` は `true` のままで、`areas` は空配列になり、`messages` に「該当 Area が無い」旨が入ります。

## 関連コマンド
- list_area_schemes
- get_areas
- get_area_params

