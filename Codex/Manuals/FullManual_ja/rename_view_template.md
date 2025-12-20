# rename_view_template

- カテゴリ: ViewOps
- 目的: このコマンドは『rename_view_template』を名称変更します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: rename_view_template

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| newName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "rename_view_template",
  "params": {
    "newName": "..."
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