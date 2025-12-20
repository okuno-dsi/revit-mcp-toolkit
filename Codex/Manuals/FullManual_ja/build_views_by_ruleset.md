# build_views_by_ruleset

- カテゴリ: ViewOps
- 目的: このコマンドは『build_views_by_ruleset』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: build_views_by_ruleset

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detachViewTemplate | bool | いいえ/状況による | true |
| keepAnnotations | bool | いいえ/状況による | false |
| sourceViewId | int | いいえ/状況による | 0 |
| withDetailing | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "build_views_by_ruleset",
  "params": {
    "detachViewTemplate": false,
    "keepAnnotations": false,
    "sourceViewId": 0,
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