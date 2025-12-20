# inspect_schedule_fields

- カテゴリ: ScheduleOps
- 目的: このコマンドは『inspect_schedule_fields』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: inspect_schedule_fields

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| samplePerField | int | いいえ/状況による | 5 |
| scheduleViewId | int | いいえ/状況による | 0 |
| title | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "inspect_schedule_fields",
  "params": {
    "samplePerField": 0,
    "scheduleViewId": 0,
    "title": "..."
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
- export_schedule_to_csv
- 