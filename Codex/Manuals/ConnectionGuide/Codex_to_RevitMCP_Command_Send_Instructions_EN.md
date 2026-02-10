# Codex to Revit MCP Command Send Procedure

## Purpose
This runbook summarises the minimum, reliable steps for exchanging JSON-RPC commands from the Codex CLI to the Revit MCP add-in. Start with the connectivity check `ping_server` to confirm reachability and responsiveness.

## Prerequisites
- Revit is running and the MCP add-in is active.
- You know the MCP port number in Revit (example: `5210`).
- Windows PowerShell 5+ or PowerShell 7+ is available.
- When you use the Python client, Python 3.x is available.

## Recommended Endpoint
- Use `http://127.0.0.1:<PORT>` (you may also use `localhost`).
- Example: `http://127.0.0.1:5210`.

## Minimal Connectivity Check (`ping_server`)
### PowerShell (two manual steps)
1) enqueue
```
Invoke-WebRequest -Method Post -Uri "http://127.0.0.1:5210/enqueue" -Headers @{ 'Content-Type'='application/json; charset=utf-8' } -Body '{"jsonrpc":"2.0","method":"ping_server","params":{},"id":1}' -SkipHttpErrorCheck
```
Successful response: `{"ok":true,"commandId":"<GUID>"}`.

2) get_result (polling)
```
$cid = '<commandId from the previous response>'
Invoke-WebRequest -Method Get -Uri "http://127.0.0.1:5210/get_result?commandId=$cid" -Headers @{ 'Accept'='application/json; charset=utf-8' } -SkipHttpErrorCheck
```
Typical success:
```
{"jsonrpc":"2.0","result":{"ok":true,"msg":"MCP Server round-trip OK (Revit Add-in reachable)" ...},"id":1}
```

### PowerShell (one-liner example: simple polling)
```
$base = "http://127.0.0.1:5210";
$h = @{ 'Content-Type'='application/json; charset=utf-8'; 'Accept'='application/json; charset=utf-8' };
$b = '{"jsonrpc":"2.0","method":"ping_server","params":{},"id":1}';
$r = Invoke-WebRequest -Method Post -Uri "$base/enqueue" -Headers $h -Body $b -SkipHttpErrorCheck;
$cid = ($r.Content | ConvertFrom-Json).commandId;
for ($i=0; $i -lt 30; $i++){ Start-Sleep -Milliseconds 500; $g = Invoke-WebRequest -Method Get -Uri "$base/get_result?commandId=$cid" -Headers $h -SkipHttpErrorCheck; if ($g.StatusCode -eq 200 -and $g.Content){ $g.Content; break } }
```

### Python client (recommended)
Use the reusable script in this repository.
```
chcp 65001 > NUL & python Scripts/Reference/send_revit_command_durable.py --port 5210 --command ping_server
```
Expected output: JSON where `result.ok == true`.

## Common Errors and Remedies
- 500 Internal Server Error (enqueue)
  - Always include `"params": {}` in the request JSON (the server requires it even when empty).
  - When Revit is busy, retry with `?force=1` appended to `/enqueue` (example: `.../enqueue?force=1`).
  - Confirm that the MCP add-in is running and the port number matches the Revit UI or log.
- Timeout (get_result polling)
  - Check whether a dialog or modal window is open inside Revit.
  - Make sure the intended project is active.
  - Inspect the `issues` field in the `ping_server` response if it exists.
- 409 Conflict (job already running)
  - Another command is still in progress. Consider `/enqueue?force=1`.

## Operational Pattern (Key Points)
- Sending: issue `POST /enqueue` with a JSON-RPC body (`jsonrpc` / `method` / `params` / `id`).
- Receiving: poll `GET /get_result?commandId=...` until you receive HTTP 200 with content (HTTP 202 or 204 means still pending).
- Success criteria: the response contains `result.ok == true` or returns an object such as `{"ok": true, ...}`.

## Reference Materials
- Command catalogue (includes `ping_server`)
  - `コマンドハンドラ一覧/Most Important コマンドハンドラ一覧（カテゴリ別）20250901_AI向け.txt`
- Reusable client
  - `Scripts/Reference/send_revit_command_durable.py`

## Reuse (Future Codex sessions)
- When you need to reconnect to the Revit MCP and send commands, follow this runbook.
- First verify reachability with `ping_server`, then send the target command with `Scripts/Reference/send_revit_command_durable.py` or the PowerShell examples above.




