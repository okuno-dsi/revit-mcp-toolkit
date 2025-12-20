# list_schedulable_fields

- カテゴリ: ScheduleOps
- 目的: このコマンドは『list_schedulable_fields』を一覧取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: list_schedulable_fields

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| categoryName | string | いいえ/状況による |  |
| scheduleViewId | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_schedulable_fields",
  "params": {
    "categoryName": "...",
    "scheduleViewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_schedule_view
- get_schedule_data
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
- export_schedule_to_csv
- delete_schedule
- 