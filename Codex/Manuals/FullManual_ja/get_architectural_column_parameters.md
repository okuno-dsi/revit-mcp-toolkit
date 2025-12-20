# get_architectural_column_parameters

- カテゴリ: ElementOps
- 目的: このコマンドは『get_architectural_column_parameters』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_architectural_column_parameters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| elementId | int | いいえ/状況による |  |
| includeDisplay | bool | いいえ/状況による | true |
| includeInstance | bool | いいえ/状況による | true |
| includeRaw | bool | いいえ/状況による | true |
| includeType | bool | いいえ/状況による | true |
| includeUnit | bool | いいえ/状況による | true |
| siDigits | int | いいえ/状況による | 3 |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_architectural_column_parameters",
  "params": {
    "count": 0,
    "elementId": 0,
    "includeDisplay": false,
    "includeInstance": false,
    "includeRaw": false,
    "includeType": false,
    "includeUnit": false,
    "siDigits": 0,
    "skip": 0
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