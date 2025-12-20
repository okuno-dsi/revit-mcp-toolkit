# replace_stacked_wall_part_type

- カテゴリ: ElementOps
- 目的: このコマンドは『replace_stacked_wall_part_type』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: replace_stacked_wall_part_type

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| childTypeId | unknown | いいえ/状況による |  |
| childTypeName | unknown | いいえ/状況による |  |
| partIndex | int | いいえ/状況による | -1 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "replace_stacked_wall_part_type",
  "params": {
    "childTypeId": "...",
    "childTypeName": "...",
    "partIndex": 0
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