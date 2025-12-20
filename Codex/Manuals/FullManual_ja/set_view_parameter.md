# set_view_parameter

- カテゴリ: ViewOps
- 目的: このコマンドは『set_view_parameter』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_view_parameter

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| detachViewTemplate | bool | いいえ/状況による | false |
| paramName | string | いいえ/状況による |  |
| value | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_view_parameter",
  "params": {
    "detachViewTemplate": false,
    "paramName": "...",
    "value": "..."
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