# sheet.remove_titleblocks_auto

- カテゴリ: ViewSheet Ops
- 目的: シート上のタイトルブロック（TitleBlock）を一括削除します（dryRun対応）。

## 概要
高レベル意図コマンド（Step 5）です。

- 正規名: `sheet.remove_titleblocks_auto`
- 従来名（互換）: `remove_titleblocks_auto`

## 使い方
- メソッド: `sheet.remove_titleblocks_auto`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| sheet | object | *いいえ | （アクティブなシートビュー） |
| dryRun | boolean | いいえ | false |
| sheetId | integer | いいえ（互換） |  |
| sheetNumber | string | いいえ（互換） |  |
| uniqueId | string | いいえ（互換） |  |

補足:
- `sheet`: `{ id } | { number } | { name } | { uniqueId }`

### リクエスト例（dryRun）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.remove_titleblocks_auto",
  "params": {
    "sheet": { "number": "A-101" },
    "dryRun": true
  }
}
```

## 関連
- create_sheet
- get_sheets
- delete_sheet
- sheet.place_view_auto

