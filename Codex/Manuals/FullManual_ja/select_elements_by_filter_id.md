# select_elements_by_filter_id

- カテゴリ: GeneralOps
- 目的: このコマンドは『select_elements_by_filter_id』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: select_elements_by_filter_id

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dryRun | bool | いいえ/状況による | false |
| levelId | int | いいえ/状況による | 0 |
| logic | string | いいえ/状況による | all |
| maxCount | int | いいえ/状況による | 5000 |
| selectionMode | string | いいえ/状況による | replace |
| viewId | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "select_elements_by_filter_id",
  "params": {
    "dryRun": false,
    "levelId": 0,
    "logic": "...",
    "maxCount": 0,
    "selectionMode": "...",
    "viewId": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_types_in_view
- select_elements
- 