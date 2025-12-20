# add_wall_layer

- カテゴリ: ElementOps
- 目的: このコマンドは『add_wall_layer』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: add_wall_layer

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| function | string | いいえ/状況による | Structure |
| insertAt | int | いいえ/状況による | 0 |
| isCore | bool | いいえ/状況による | false |
| isVariable | bool | いいえ/状況による | false |
| materialId | int | いいえ/状況による | 0 |
| materialName | string | いいえ/状況による |  |
| thicknessMm | number | いいえ/状況による | 1.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "add_wall_layer",
  "params": {
    "function": "...",
    "insertAt": 0,
    "isCore": false,
    "isVariable": false,
    "materialId": 0,
    "materialName": "...",
    "thicknessMm": 0.0
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