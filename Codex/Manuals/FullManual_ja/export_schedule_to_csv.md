# export_schedule_to_csv

- カテゴリ: ScheduleOps
- 目的: このコマンドは『export_schedule_to_csv』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_schedule_to_csv

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| delimiter | string | いいえ/状況による |  |
| fillBlanks | bool | いいえ/状況による | false |
| includeHeader | bool | いいえ/状況による | true |
| itemize | bool | いいえ/状況による | false |
| newline | string | いいえ/状況による |  |
| outputFilePath | string | いいえ/状況による |  |
| scheduleViewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_schedule_to_csv",
  "params": {
    "delimiter": "...",
    "fillBlanks": false,
    "includeHeader": false,
    "itemize": false,
    "newline": "...",
    "outputFilePath": "...",
    "scheduleViewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_schedule_view
- get_schedule_data
- list_schedulable_fields
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
- delete_schedule
- 