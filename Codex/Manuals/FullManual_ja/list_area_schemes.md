# list_area_schemes

- カテゴリ: Area
- 目的: アクティブな Revit ドキュメントに存在するすべての AreaScheme を一覧表示し、必要に応じて各スキームに属する Area 数を返します。

## 概要
このコマンドは JSON-RPC 経由で Revit MCP アドインに対して実行されます。各種 Area 関連コマンドで参照するための `AreaScheme` 情報を簡潔な JSON で取得できます。

## 使い方
- メソッド: list_area_schemes

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeCounts | bool | いいえ | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_area_schemes",
  "params": {
    "includeCounts": true
  }
}
```

### 結果例（成功）
```jsonc
{
  "ok": true,
  "areaSchemes": [
    { "id": 101, "name": "Gross Building", "areaCount": 42 },
    { "id": 102, "name": "Rentable",       "areaCount": 35 }
  ],
  "messages": [
    "2 AreaSchemes found."
  ]
}
```

## 関連コマンド
- get_area_schemes
- get_areas_by_scheme

