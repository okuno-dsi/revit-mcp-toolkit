# sync_view_state

- カテゴリ: ViewOps
- 目的: このコマンドは『sync_view_state』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: sync_view_state

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dstViewId | int | いいえ/状況による |  |
| srcViewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sync_view_state",
  "params": {
    "dstViewId": 0,
    "srcViewId": 0
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