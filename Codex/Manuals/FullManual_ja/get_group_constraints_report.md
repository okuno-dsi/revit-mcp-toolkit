# get_group_constraints_report

- カテゴリ: GroupOps
- 目的: このコマンドは『get_group_constraints_report』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_group_constraints_report

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| groupId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_group_constraints_report",
  "params": {
    "groupId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_group_types
- get_group_info
- get_element_group_membership
- get_groups_in_view
- get_group_members
- 