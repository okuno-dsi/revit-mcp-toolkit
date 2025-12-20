# audit_hidden_in_view

- カテゴリ: ViewOps
- 目的: このコマンドは『audit_hidden_in_view』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: audit_hidden_in_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeCategoryStates | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "audit_hidden_in_view",
  "params": {
    "includeCategoryStates": false
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