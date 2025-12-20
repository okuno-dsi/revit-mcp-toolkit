# get_groups

- カテゴリ: GroupOps
- 目的: このコマンドは『get_groups』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_groups

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による | 100 |
| skip | int | いいえ/状況による | 0 |
| viewScoped | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_groups",
  "params": {
    "count": 0,
    "skip": 0,
    "viewScoped": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_group_info
- get_element_group_membership
- get_groups_in_view
- get_group_members
- get_group_constraints_report
- 