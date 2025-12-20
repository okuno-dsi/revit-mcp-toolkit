# set_category_override

- カテゴリ: ViewOps
- 目的: このコマンドは『set_category_override』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_category_override

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| applyFill | bool | いいえ/状況による | false |
| autoWorkingView | bool | いいえ/状況による | true |
| batchSize | int | いいえ/状況による | 200 |
| detachViewTemplate | bool | いいえ/状況による | false |
| maxMillisPerTx | int | いいえ/状況による | 3000 |
| refreshView | bool | いいえ/状況による | true |
| startIndex | int | いいえ/状況による | 0 |
| transparency | int | いいえ/状況による | 40 |
| viewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_override",
  "params": {
    "applyFill": false,
    "autoWorkingView": false,
    "batchSize": 0,
    "detachViewTemplate": false,
    "maxMillisPerTx": 0,
    "refreshView": false,
    "startIndex": 0,
    "transparency": 0,
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