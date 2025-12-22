# viewport.move_to_sheet_center

- カテゴリ: ViewSheet Ops
- 目的: ビューポートをシート中心に移動します（dryRun対応）。

## 概要
高レベル意図コマンド（Step 5）です。

- 正規名: `viewport.move_to_sheet_center`
- 従来名（互換）: `viewport_move_to_sheet_center`

## 使い方
- メソッド: `viewport.move_to_sheet_center`

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| viewportId | integer | *はい |  |
| sheet | object | いいえ（代替） |  |
| viewId | integer | いいえ（代替） |  |
| dryRun | boolean | いいえ | false |

補足:
- `viewportId` を省略する場合は、`sheet` + `viewId` でシート上の該当ビューポートを探索します。
- `sheet`: `{ id } | { number } | { name } | { uniqueId }`
- シート中心は、可能なら `SHEET_WIDTH/HEIGHT` を使用し、0/不明の場合はタイトルブロックの外形（バウンディングボックス）の中心にフォールバックします。

### リクエスト例（dryRun）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "viewport.move_to_sheet_center",
  "params": {
    "viewportId": 123456,
    "dryRun": true
  }
}
```

## 関連
- sheet.place_view_auto
- place_view_on_sheet
- get_view_placements
