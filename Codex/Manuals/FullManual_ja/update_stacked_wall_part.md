# update_stacked_wall_part

- カテゴリ: ElementOps
- 目的: このコマンドは『update_stacked_wall_part』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_stacked_wall_part

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| childTypeId | unknown | いいえ/状況による |  |
| childTypeName | unknown | いいえ/状況による |  |
| heightMm | unknown | いいえ/状況による |  |
| partIndex | int | いいえ/状況による | -1 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_stacked_wall_part",
  "params": {
    "childTypeId": "...",
    "childTypeName": "...",
    "heightMm": "...",
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