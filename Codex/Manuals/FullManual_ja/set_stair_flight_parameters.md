# set_stair_flight_parameters

- カテゴリ: ElementOps
- 目的: このコマンドは『set_stair_flight_parameters』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_stair_flight_parameters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| flightIndex | int | いいえ/状況による |  |
| paramName | string | いいえ/状況による |  |
| value | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_stair_flight_parameters",
  "params": {
    "elementId": 0,
    "flightIndex": 0,
    "paramName": "...",
    "value": "..."
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