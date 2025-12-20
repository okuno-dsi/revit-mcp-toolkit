# create_elevation_view

- カテゴリ: ViewOps
- 目的: このコマンドは『create_elevation_view』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_elevation_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| levelName | string | いいえ/状況による |  |
| name | string | いいえ/状況による | 新しい軸組図 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_elevation_view",
  "params": {
    "levelName": "...",
    "name": "..."
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
- compare_view_states
- get_views
- 