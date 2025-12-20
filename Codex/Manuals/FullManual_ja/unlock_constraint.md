# unlock_constraint

- カテゴリ: ConstraintOps
- 目的: このコマンドは『unlock_constraint』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: unlock_constraint

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| {key} | unknown | はい |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "unlock_constraint",
  "params": {
    "{key}": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- set_alignment_constraint
- update_dimension_value_if_temp_dim
- 