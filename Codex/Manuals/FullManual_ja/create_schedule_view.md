# create_schedule_view

- カテゴリ: ScheduleOps
- 目的: このコマンドは『create_schedule_view』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_schedule_view

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| addElementId | bool | いいえ/状況による | false |
| categoryName | string | いいえ/状況による |  |
| fieldNames | array | いいえ/状況による |  |
| fieldParamIds | array | いいえ/状況による |  |
| isItemized | bool | いいえ/状況による |  |
| title | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_schedule_view",
  "params": {
    "addElementId": false,
    "categoryName": "...",
    "fieldNames": "...",
    "fieldParamIds": "...",
    "isItemized": false,
    "title": "..."
  }
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