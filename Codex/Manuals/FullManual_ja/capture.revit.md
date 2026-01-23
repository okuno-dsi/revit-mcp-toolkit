# capture.revit

- カテゴリ: MetaOps
- 種別: read
- 目的: Revit のウィンドウ／ダイアログをキャプチャします（サーバー側のみ／Revitキュー非使用）。

## 概要
プロセス名 `Revit` のトップレベルウィンドウ（表示中）を探索してキャプチャします。

これは **サーバー側のみ**で完結し、Revit API は呼びません。

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| target | string | no | "active_dialogs" |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| preferPrintWindow | bool | no | true |
| includeSha256 | bool | no | false |
| ocr | bool | no | false |
| ocrLang | string | no | (auto) |

`target` の値：
- `"active_dialogs"`: Revit ダイアログ（`className == "#32770"`）
- `"main"`: 表示中の Revit 非ダイアログのうち最大のウィンドウ
- `"floating_windows"`: メイン以外の表示中 Revit 非ダイアログ
- `"all_visible"`: 表示中の Revit トップレベルウィンドウすべて

## OCR（任意）
- `ocr: true` で Tesseract OCR を試行します（best effort）。
- `ocrLang` は Tesseract の言語コードを指定します（例: `"jpn+eng"`, `"eng"`）。省略時は `"jpn+eng"` です。
- OCR には `capture-agent/tessdata/*.traineddata` が必要です。無い場合は `ocr.status` が `tessdata_missing` または `langdata_missing` になります。
- 大きな画像はスキップされます（モデル／シートの誤OCR防止）。

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "capture.revit",
  "params": {
    "target": "active_dialogs"
  }
}
```

## 結果
- `captures[]`: `{ path, hwnd, process, title, className, bounds:{x,y,w,h}, risk, sha256?, ocr? }`

## 注意（安全／同意）
Revit の大きいウィンドウは、モデル／シートの表示を含む可能性が高く（OCRリスク高）、機微情報が含まれ得ます。AIサービスへ送る前に必ず明示的な同意確認（consent gate）を挟んでください。

## 関連
- capture.list_windows
- capture.window
- capture.screen
