# export_schedule_to_excel

- カテゴリ: ScheduleOps
- 目的: このコマンドは『export_schedule_to_excel』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_schedule_to_excel

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| autoFit | bool | いいえ/状況による | true |
| filePath | string | いいえ/状況による |  |
| viewId | int | いいえ/状況による |  |
| viewName | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_schedule_to_excel",
  "params": {
    "autoFit": false,
    "filePath": "...",
    "viewId": 0,
    "viewName": "..."
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