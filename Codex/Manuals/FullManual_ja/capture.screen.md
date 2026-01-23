# capture.screen

- カテゴリ: MetaOps
- 種別: read
- 目的: モニターのスクリーンショットを取得します（サーバー側のみ／Revitキュー非使用）。

## 概要
デスクトップの画面取得で 1 つ以上のモニターをキャプチャします。

これは **サーバー側のみ**で完結し、Revit API は呼びません。

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| monitorIndex | number | no | （全モニター） |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| includeSha256 | bool | no | false |

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "capture.screen",
  "params": {
    "monitorIndex": 0
  }
}
```

## 結果
- `captures[]`: `{ path, monitorIndex, device, bounds:{x,y,w,h}, risk, sha256? }`
  - `risk` はスクリーンキャプチャなので常に `"high"` です。

## 関連
- capture.list_windows
- capture.window
- capture.revit

