# get_current_view

- カテゴリ: ViewOps
- 目的: このコマンドは『get_current_view』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_current_view

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_current_view",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- save_view_state
- restore_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- get_views
- 