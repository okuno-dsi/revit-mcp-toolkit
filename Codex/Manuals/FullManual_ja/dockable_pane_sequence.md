# dockable_pane_sequence

- カテゴリ: RevitUI
- 目的: このコマンドは『dockable_pane_sequence』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: dockable_pane_sequence

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| continueOnError | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "dockable_pane_sequence",
  "params": {
    "continueOnError": false
  }
}
```

## 関連コマンド
## 関連コマンド
- activate_view
- open_views
- close_inactive_views
- close_views
- close_views_except
- tile_windows
- list_dockable_panes
- 