# create_perspective_view

- カテゴリ: ViewOps
- 目的: このコマンドは『create_perspective_view』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_perspective_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| name | string | いいえ/状況による |  |
| templateViewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_perspective_view",
  "params": {
    "name": "...",
    "templateViewId": 0
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