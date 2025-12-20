# get_group_info

- カテゴリ: GroupOps
- 目的: このコマンドは『get_group_info』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_group_info

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| includeMembers | bool | いいえ/状況による | false |
| includeOwnerView | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_group_info",
  "params": {
    "elementId": 0,
    "includeMembers": false,
    "includeOwnerView": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_group_types
- get_element_group_membership
- get_groups_in_view
- get_group_members
- get_group_constraints_report
- 