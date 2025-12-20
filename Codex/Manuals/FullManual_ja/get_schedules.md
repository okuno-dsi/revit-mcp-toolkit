# get_schedules

- カテゴリ: ScheduleOps
- 目的: このコマンドは『get_schedules』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_schedules

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_schedules",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- get_schedule_data
- list_schedulable_fields
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
- export_schedule_to_csv
- delete_schedule
- 