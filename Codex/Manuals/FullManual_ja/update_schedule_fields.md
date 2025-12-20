# update_schedule_fields

- カテゴリ: ScheduleOps
- 目的: このコマンドは『update_schedule_fields』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_schedule_fields

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| addElementId | bool | いいえ/状況による | false |
| isItemized | bool | いいえ/状況による |  |
| scheduleViewId | int | いいえ/状況による |  |
| showGrandTotals | bool | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_schedule_fields",
  "params": {
    "addElementId": false,
    "isItemized": false,
    "scheduleViewId": 0,
    "showGrandTotals": false
  }
}
```

## 関連コマンド
## 関連コマンド
- create_schedule_view
- get_schedule_data
- list_schedulable_fields
- update_schedule_filters
- update_schedule_sorting
- export_schedule_to_csv
- delete_schedule
- 