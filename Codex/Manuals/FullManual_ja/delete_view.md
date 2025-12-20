# delete_view

- カテゴリ: ViewOps
- 目的: このコマンドは『delete_view』を削除します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: delete_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| closeOpenUiViews | bool | いいえ/状況による | true |
| fast | bool | いいえ/状況による | true |
| refreshView | bool | いいえ/状況による | false |
| useTempDraftingView | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "delete_view",
  "params": {
    "closeOpenUiViews": false,
    "fast": false,
    "refreshView": false,
    "useTempDraftingView": false
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