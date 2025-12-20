# get_material_parameters

- カテゴリ: ElementOps
- 目的: このコマンドは『get_material_parameters』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_material_parameters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| materialId | int | いいえ/状況による | 0 |
| namesOnly | bool | いいえ/状況による | false |
| skip | int | いいえ/状況による | 0 |
| summaryOnly | bool | いいえ/状況による | false |
| uniqueId | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_material_parameters",
  "params": {
    "count": 0,
    "materialId": 0,
    "namesOnly": false,
    "skip": 0,
    "summaryOnly": false,
    "uniqueId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- list_material_parameters
- update_material_parameter
- duplicate_material
- rename_material
- delete_material
- create_material
- apply_material_to_element
- 