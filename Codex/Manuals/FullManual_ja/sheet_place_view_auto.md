# sheet.place_view_auto

- カテゴリ: ViewSheet Ops
- 目的: シートにビューを配置し、「既に他シートに配置済み」などの制約を自動処理（複製して配置）します。

## 概要
高レベル意図コマンド（Step 5）です。

- 正規名: `sheet.place_view_auto`
- 従来名（互換）: `place_view_on_sheet_auto`

動作:
- 通常ビュー → `Viewport` を作成
- 集計表（`ViewSchedule`） → `ScheduleSheetInstance` を作成

## 使い方
- メソッド: `sheet.place_view_auto`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| sheet | object | *いいえ | （アクティブなシートビュー） |
| view | object | *はい |  |
| placement | object | いいえ | `{ "mode": "center" }` |
| ifAlreadyPlaced | string | いいえ | `duplicate_dependent` |
| dryRun | boolean | いいえ | false |
| sheetId | integer | いいえ（互換） |  |
| sheetNumber | string | いいえ（互換） |  |
| uniqueId | string | いいえ（互換） |  |
| viewId | integer | いいえ（互換） |  |
| viewUniqueId | string | いいえ（互換） |  |
| centerOnSheet | boolean | いいえ（互換） | false |
| location | object | いいえ（互換） |  |

補足:
- `sheet`: `{ id } | { number } | { name } | { uniqueId }`
- `view`: `{ id } | { name } | { uniqueId }`
- `placement.mode`: `center` / `point`
- `placement.pointMm`: `mode="point"` のとき `{ x, y }`（mm）
- シート中心への配置は、可能なら `SHEET_WIDTH/HEIGHT` を使用し、0/不明の場合はタイトルブロックの外形（バウンディングボックス）の中心にフォールバックします。
- `placement` を省略するとシート中心に配置します（警告として返します）
- `ifAlreadyPlaced`:
  - `error`: `CONSTRAINT_VIEW_ALREADY_PLACED` を返す
  - `duplicate`: 通常複製（Duplicate）して配置
  - `duplicate_dependent`: 依存複製（AsDependent）を試し、不可なら通常複製にフォールバック（警告）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.place_view_auto",
  "params": {
    "sheet": { "number": "A-101" },
    "view": { "name": "Level 1 - Plan" },
    "placement": { "mode": "center" },
    "ifAlreadyPlaced": "duplicate_dependent",
    "dryRun": false
  }
}
```

## 関連
- place_view_on_sheet
- replace_view_on_sheet
- get_view_placements
- viewport.move_to_sheet_center
- sheet.remove_titleblocks_auto
