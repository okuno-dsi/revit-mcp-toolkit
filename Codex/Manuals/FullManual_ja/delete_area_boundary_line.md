# delete_area_boundary_line

- カテゴリ: Area
- 目的: このコマンドは『delete_area_boundary_line』を削除します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: delete_area_boundary_line

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 50 |
| elementId | int | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 100 |
| refreshView | bool | いいえ/状況による | false |
| startIndex | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_area_boundary_line",
  "params": {
    "batchSize": 0,
    "elementId": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- 