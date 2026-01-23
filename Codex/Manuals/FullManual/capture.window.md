# capture.window

- Category: MetaOps
- Kind: read
- Purpose: Capture a screenshot of a specific window by HWND (server-side, no Revit queue).

## Overview
Captures a window image using:
1) `PrintWindow` (best-effort), then
2) Desktop capture fallback (requires the window to be visible on screen)

This is **server-local** and does not call the Revit API.

## Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| hwnd | string \| number | yes |  |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| preferPrintWindow | bool | no | true |
| includeSha256 | bool | no | false |

## Example Request
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

## Result
- `captures[]`: `{ path, hwnd, process, title, className, bounds:{x,y,w,h}, risk, sha256? }`
  - `risk`: `"low"` for typical dialogs, `"high"` for large Revit windows / screen-like captures.

## Related
- capture.list_windows
- capture.screen
- capture.revit

