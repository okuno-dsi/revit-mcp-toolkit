# move_door

- カテゴリ: ElementOps
- 目的: このコマンドは『move_door』を移動します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: move_door

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dx | number | いいえ/状況による | 0.0 |
| dy | number | いいえ/状況による | 0.0 |
| dz | number | いいえ/状況による | 0.0 |
| elementId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_door",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0
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