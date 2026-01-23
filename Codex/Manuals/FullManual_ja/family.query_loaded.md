# family.query_loaded

- カテゴリ: Family
- 目的: 読み込まれているファミリ/タイプを高速に検索（ロード可能シンボル・インプレイス・任意でシステムタイプ）
- 新規: 2026-01-22

## 概要
インスタンスを走査せず、読み込み済みの定義（タイプ）だけを高速に列挙・検索します。

## 使い方
- メソッド: family.query_loaded

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| q | string | いいえ | "" |
| category | string | いいえ | "" |
| builtInCategory | string | いいえ | "" |
| includeLoadableSymbols | bool | いいえ | true |
| includeInPlaceFamilies | bool | いいえ | true |
| includeSystemTypes | bool | いいえ | false |
| systemTypeCategoryWhitelist | string[] | いいえ |  |
| limit | int | いいえ | 50 |
| offset | int | いいえ | 0 |

### リクエスト例
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

### 戻り値（形）
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

## 注意
- `q` は分割トークンの部分一致です（すべてのトークンを含むものが対象）。
- `category` は表示名、`builtInCategory` は `BuiltInCategory` 名（例: `OST_Doors`）で指定します。
- システムタイプは `includeSystemTypes=true` の場合のみ返り、さらに `systemTypeCategoryWhitelist` で絞り込めます。

## 関連
- get_family_types
- get_family_instances
- summarize_family_types_by_category
