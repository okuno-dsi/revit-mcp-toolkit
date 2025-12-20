# create_interior_elevation

- カテゴリ: ViewOps
- 目的: このコマンドは『create_interior_elevation』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_interior_elevation

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| facing | string | いいえ/状況による |  |
| hostViewId | int | いいえ/状況による | 0 |
| name | string | いいえ/状況による |  |
| scale | int | いいえ/状況による | 100 |
| slotIndex | int | いいえ/状況による | -1 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_interior_elevation",
  "params": {
    "facing": "...",
    "hostViewId": 0,
    "name": "...",
    "scale": 0,
    "slotIndex": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- 