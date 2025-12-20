# set_category_visibility

- カテゴリ: ViewOps
- 目的: このコマンドは『set_category_visibility』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_category_visibility

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| categoryIds | unknown | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |
| visible | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_visibility",
  "params": {
    "categoryIds": "...",
    "viewId": 0,
    "visible": false
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