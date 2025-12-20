# restore_view_state

- カテゴリ: ViewOps
- 目的: このコマンドは『restore_view_state』を復元します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: restore_view_state

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "restore_view_state",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- get_view_info
- save_view_state
- create_view_plan
- create_section
- create_elevation_view
- compare_view_states
- get_views
- 