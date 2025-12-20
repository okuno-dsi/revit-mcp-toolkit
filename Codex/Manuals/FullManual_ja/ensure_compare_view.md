# ensure_compare_view

- カテゴリ: ViewOps
- 目的: このコマンドは『ensure_compare_view』を保証します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: ensure_compare_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baseViewId | int | いいえ/状況による | 0 |
| desiredName | string | いいえ/状況による |  |
| detachTemplate | bool | いいえ/状況による | true |
| onNameConflict | string | いいえ/状況による | returnExisting |
| withDetailing | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "ensure_compare_view",
  "params": {
    "baseViewId": 0,
    "desiredName": "...",
    "detachTemplate": false,
    "onNameConflict": "...",
    "withDetailing": false
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