# capture.list_windows

- Category: MetaOps
- Kind: read
- Purpose: List visible top-level Windows desktop windows (server-side, no Revit queue).

## Overview
Enumerates top-level windows via Win32 (`EnumWindows`) and returns basic metadata.

This is **server-local**:
- It does not call the Revit API.
- It works even when Revit is busy.

## Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| processName | string | no |  |
| titleContains | string | no |  |
| visibleOnly | bool | no | true |

## Example Request
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "capture.list_windows",
  "params": {
    "processName": "Revit",
    "titleContains": "エラー",
    "visibleOnly": true
  }
}
```

## Result
- `windows[]`: `{ hwnd, pid, process, title, className, bounds:{x,y,w,h} }`

## Related
- capture.window
- capture.screen
- capture.revit

