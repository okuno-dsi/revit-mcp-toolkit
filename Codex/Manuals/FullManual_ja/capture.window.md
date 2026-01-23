# capture.window

- カテゴリ: MetaOps
- 種別: read
- 目的: 指定したウィンドウ（HWND）をスクリーンショットします（サーバー側のみ／Revitキュー非使用）。

## 概要
以下の順でベストエフォートにキャプチャします：
1) `PrintWindow`（可能なら）  
2) 画面からの取得（ウィンドウが画面上に見えている必要があります）

これは **サーバー側のみ**で完結し、Revit API は呼びません。

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| hwnd | string \| number | yes |  |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| preferPrintWindow | bool | no | true |
| includeSha256 | bool | no | false |

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "capture.window",
  "params": {
    "hwnd": "0x00123456",
    "preferPrintWindow": true
  }
}
```

## 結果
- `captures[]`: `{ path, hwnd, process, title, className, bounds:{x,y,w,h}, risk, sha256? }`
  - `risk`: 典型的なダイアログは `"low"`、大きい Revit ウィンドウや画面に近いものは `"high"`。

## 関連
- capture.list_windows
- capture.screen
- capture.revit

