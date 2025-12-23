# sheet.inspect

- カテゴリ: Diagnostics
- 目的: シートの状態を点検します（タイトルブロック、シート外形、ビューポート一覧）。

## 概要
- Canonical: `sheet.inspect`
- 旧エイリアス: `sheet_inspect`

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---:|---:|
| sheet | object | no | アクティブシート |
| sheetId | integer | no | アクティブシート |
| sheetNumber | string | no | アクティブシート |
| includeTitleblocks | boolean | no | true |
| includeViewports | boolean | no | true |

推奨: `sheet` オブジェクト
- `{ id }`
- `{ uniqueId }`
- `{ number }`（シート番号）
- `{ name }`

## リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "sheet.inspect",
  "params": { "sheet": { "number": "A-101" } }
}
```

## 出力（概要）
- `data.sheet`: sheetId/number/name/uniqueId
- `data.outlineMm`: シート外形（mm、min/max/width/height）
- `data.needsFallbackSheetSize`: 外形サイズが信頼できない場合 `true`
- `data.titleblocks`: 件数 + タイプ情報（任意）
- `data.viewports`: ビューポート一覧（viewportId/viewId/中心/外形/回転）

