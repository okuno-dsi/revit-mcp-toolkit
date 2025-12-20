# get_views

- カテゴリ: ViewOps
- 目的: このコマンドは『get_views』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_views

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detail | bool | いいえ/状況による | false |
| includeTemplates | bool | いいえ/状況による | false |
| nameContains | string | いいえ/状況による |  |
| viewType | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_views",
  "params": {
    "detail": false,
    "includeTemplates": false,
    "nameContains": "...",
    "viewType": "..."
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