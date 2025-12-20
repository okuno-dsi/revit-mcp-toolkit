# update_schedule_filters

- カテゴリ: ScheduleOps
- 目的: このコマンドは『update_schedule_filters』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_schedule_filters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| scheduleViewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_schedule_filters",
  "params": {
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
- update_schedule_sorting
- export_schedule_to_csv
- delete_schedule
- 