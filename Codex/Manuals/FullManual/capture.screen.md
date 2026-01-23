# capture.screen

- Category: MetaOps
- Kind: read
- Purpose: Capture monitor screenshots (server-side, no Revit queue).

## Overview
Captures one or more monitors using desktop capture.

This is **server-local** and does not call the Revit API.

## Parameters
| Name | Type | Required | Default |
|---|---|---|---|
| monitorIndex | number | no | (all monitors) |
| outDir | string | no | `%LOCALAPPDATA%\\RevitMCP\\captures` |
| includeSha256 | bool | no | false |

## Example Request
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

## Result
- `captures[]`: `{ path, monitorIndex, device, bounds:{x,y,w,h}, risk, sha256? }`
  - `risk` is always `"high"` for screen captures.

## Related
- capture.list_windows
- capture.window
- capture.revit

