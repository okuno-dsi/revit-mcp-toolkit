# get_architectural_columns

- カテゴリ: ElementOps
- 目的: このコマンドは『get_architectural_columns』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_architectural_columns

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| bbox | unknown | いいえ/状況による |  |
| count | int | いいえ/状況による |  |
| levelId | int | いいえ/状況による |  |
| skip | int | いいえ/状況による | 0 |
| sortBy | string | いいえ/状況による | id |
| summaryOnly | bool | いいえ/状況による | false |
| typeId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_architectural_columns",
  "params": {
    "bbox": "...",
    "count": 0,
    "levelId": 0,
    "skip": 0,
    "sortBy": "...",
    "summaryOnly": false,
    "typeId": 0
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