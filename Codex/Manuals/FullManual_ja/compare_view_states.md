# compare_view_states

- カテゴリ: ViewOps
- 目的: このコマンドは『compare_view_states』を比較します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: compare_view_states

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baselineViewId | int | いいえ/状況による | 0 |
| includeCategories | bool | いいえ/状況による | true |
| includeFilters | bool | いいえ/状況による | true |
| includeHiddenElements | bool | いいえ/状況による | false |
| includeWorksets | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "compare_view_states",
  "params": {
    "baselineViewId": 0,
    "includeCategories": false,
    "includeFilters": false,
    "includeHiddenElements": false,
    "includeWorksets": false
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
- get_views
- 