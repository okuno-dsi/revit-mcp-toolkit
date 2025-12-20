# get_family_types

- カテゴリ: ElementOps
- 目的: このコマンドは『get_family_types』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_family_types

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| categoryIds | unknown | いいえ/状況による |  |
| categoryNames | unknown | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| nameContains | string | いいえ/状況による |  |
| namesOnly | bool | いいえ/状況による | false |
| skip | int | いいえ/状況による | 0 |
| summaryOnly | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_family_types",
  "params": {
    "categoryIds": "...",
    "categoryNames": "...",
    "count": 0,
    "nameContains": "...",
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_material_parameters
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material
- 