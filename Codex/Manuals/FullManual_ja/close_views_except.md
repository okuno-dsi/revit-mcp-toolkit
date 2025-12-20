# close_views_except

- カテゴリ: RevitUI
- 目的: このコマンドは『close_views_except』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: close_views_except

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "close_views_except",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- activate_view
- open_views
- close_inactive_views
- close_views
- tile_windows
- list_dockable_panes
- show_dockable_pane
- 