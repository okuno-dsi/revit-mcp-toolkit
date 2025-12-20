# create_window_on_wall

- カテゴリ: ElementOps
- 目的: このコマンドは『create_window_on_wall』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_window_on_wall

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| typeId | unknown | いいえ/状況による |  |
| typeName | unknown | いいえ/状況による |  |
| wallId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_window_on_wall",
  "params": {
    "typeId": "...",
    "typeName": "...",
    "wallId": 0
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