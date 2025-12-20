# reorder_schedule_fields

- カテゴリ: ScheduleOps
- 目的: 集計表（ViewSchedule）の列（フィールド）の表示順を変更します。

## 概要
Revit の集計表の列順は `ScheduleDefinition` のフィールド順で管理されています。このコマンドは `ScheduleDefinition.SetFieldOrder`（リフレクション経由）で **既存フィールドを再作成せず** に順序だけを変更します。既存の `ScheduleFieldId` を維持するため、フィルタや並び替えが参照しているフィールドIDが壊れにくい方式です。

## 使い方
- メソッド: reorder_schedule_fields

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| scheduleViewId | int | いいえ* |  |
| order | array | いいえ* |  |
| moveColumn | string | いいえ* |  |
| beforeColumn | string | いいえ* |  |
| moveIndex | int | いいえ* |  |
| beforeIndex | int | いいえ* |  |
| includeHidden | bool | いいえ | false |
| appendUnspecified | bool | いいえ | true |
| strict | bool | いいえ | false |
| visibleOnly | bool | いいえ | true |
| returnFields | bool | いいえ | *(order時: true / move時: false)* |

\* 必須の組み合わせ:
- `order` を指定するか、移動ペア（`moveColumn`+`beforeColumn` または `moveIndex`+`beforeIndex`）を指定してください。
- `scheduleViewId` は、アクティブビューが集計表なら省略できます。

### 速い移動モード
- `moveColumn` / `beforeColumn` に Excel 風の列文字（A..Z, AA..）を使えます（例: N を H の前へ）。

### `order` の要素
各要素は次のいずれか:
- **string**: フィールド名（`ScheduleField.GetName()`）または列見出し（`ScheduleField.ColumnHeading`）で一致
- **int**: `paramId`（`ScheduleField.ParameterId.IntegerValue`）で一致
- **object**: `{ "paramId": 123 }` / `{ "heading": "..." }` / `{ "name": "..." }`（paramId → heading → name の順で一致）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reorder_schedule_fields",
  "params": {
    "scheduleViewId": 12345,
    "order": ["タイプ マーク", "ファミリとタイプ", "コメント"],
    "appendUnspecified": true,
    "includeHidden": false,
    "strict": false
  }
}
```

### リクエスト例（速い移動・アクティブ集計表）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "reorder_schedule_fields",
  "params": {
    "moveColumn": "N",
    "beforeColumn": "H"
  }
}
```

## 関連コマンド
- get_current_view
- get_schedules
- inspect_schedule_fields
- update_schedule_fields
- update_schedule_filters
- update_schedule_sorting
