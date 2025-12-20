# hide_elements_in_view

- カテゴリ: ViewOps
- 目的: このコマンドは『hide_elements_in_view』を非表示します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: hide_elements_in_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による | 800 |
| detachViewTemplate | bool | いいえ/状況による | false |
| elementId | int | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 4000 |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "hide_elements_in_view",
  "params": {
    "batchSize": 0,
    "detachViewTemplate": false,
    "elementId": 0,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "viewId": 0
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