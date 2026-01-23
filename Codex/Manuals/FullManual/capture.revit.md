# capture.revit

- Category: MetaOps
- Kind: read
- Purpose: Capture Revit windows/dialogs (server-side, no Revit queue).

## Overview
Finds visible Revit top-level windows (process name `Revit`) and captures them.

This is **server-local** and does not call the Revit API.

## Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| target | string | no | "active_dialogs" |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| preferPrintWindow | bool | no | true |
| includeSha256 | bool | no | false |
| ocr | bool | no | false |
| ocrLang | string | no | (auto) |

`target` values:
- `"active_dialogs"`: Revit dialogs (`className == "#32770"`)
- `"main"`: largest visible non-dialog Revit window
- `"floating_windows"`: visible non-dialog Revit windows excluding the main window
- `"all_visible"`: all visible Revit top-level windows

## OCR (optional)
- `ocr: true` attempts Tesseract OCR on captures (best effort).
- `ocrLang` uses Tesseract language codes (example: `"jpn+eng"`, `"eng"`). If omitted, default is `"jpn+eng"`.
- OCR requires `capture-agent/tessdata/*.traineddata`. If missing, `ocr.status` reports `tessdata_missing` or `langdata_missing`.
- Large images are skipped to reduce accidental OCR of model/sheet canvases.

## Example Request
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

## Result
- `captures[]`: `{ path, hwnd, process, title, className, bounds:{x,y,w,h}, risk, sha256?, ocr? }`

## Notes (Safety / Consent)
These captures can include model/sheet views (high OCR risk). Treat returned images as sensitive and use an explicit consent gate before sending them to an AI service.

## Related
- capture.list_windows
- capture.window
- capture.screen
