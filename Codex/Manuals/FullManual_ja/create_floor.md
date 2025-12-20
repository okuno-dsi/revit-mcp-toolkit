# create_floor

- カテゴリ: ElementOps
- 目的: このコマンドは『create_floor』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_floor

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| floorTypeName | string | いいえ/状況による |  |
| isStructural | bool | いいえ/状況による | false |
| levelName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_floor",
  "params": {
    "floorTypeName": "...",
    "isStructural": false,
    "levelName": "..."
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