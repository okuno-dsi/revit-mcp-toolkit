# Revit Connection One‑Shot Quickstart (EN)

This guide shows a reliable, single‑pass setup to connect an AI client to Revit through a safe forwarding chain, with commands you can paste into PowerShell on Windows.

Topology:

Client → Proxy (Python) → Playbook (C# .NET 8) → RevitMCPServer (inside Revit)

Default ports and policy:

- RevitMCPServer: 5210 (first Revit), 5211/5212 for additional instances
- Playbook Server: 5209 (fixed)
- Proxy: 5221 (avoid conflicts with 5211/5212)

Tested with:

- Windows 11, .NET SDK 8/9 installed
- Python 3.10+
- Repo layout under `VS2022/Ver431`

---

## 1) Prerequisites

- Revit with RevitMCP Add‑in installed (start Revit; the add‑in hosts `RevitMCPServer` on 5210+).
- .NET SDK 8.0+ available (`dotnet --info`).
- Python 3.9+ (`python -V`).

Optional but recommended:

- PowerShell 7 (`pwsh`), Administrator privileges are not required for localhost.

---

## 2) Start Revit (RevitMCPServer)

1. Launch Revit normally.
2. Verify the MCP port(s):

   ````powershell
   Get-NetTCPConnection -LocalPort 5210,5211,5212 -State Listen | 
     Select-Object LocalPort,OwningProcess,@{N='Name';E={(Get-Process -Id $_.OwningProcess).ProcessName}}
   ````

You should see `RevitMCPServer` (or `dotnet`) listening on one of 5210/5211/5212.

---

## 3) Start the Playbook Server (port 5209)

Playbook forwards JSON‑RPC to a specific Revit MCP instance and records/replays operations with guardrails.

Example (forwarding to the primary Revit instance at 5210):

````powershell
dotnet run --project "C:\Users\okuno\Documents\VS2022\Ver431\McpPlaybookServer\src\McpPlaybookServer" -- --port 5209 --forward http://127.0.0.1:5210
````

Health check:

````powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5209/teach/start?name=quickstart" -Method Post
````

Expected: `{ "ok": true, "dir": "...\RevitMCP\Playbooks\quickstart" }`

Tip: To target a different Revit instance per request without restarting, use dynamic routes:

- `POST http://127.0.0.1:5209/t/{revitPort}/rpc`
- `POST http://127.0.0.1:5209/t/{revitPort}/enqueue`

---

## 4) Start the Proxy (port 5221)

The proxy logs every request/response to JSONL while transparently forwarding to the Playbook.

Run in the `ChatRevit` folder:

````powershell
Set-Location "C:\Users\okuno\Documents\VS2022\Ver431\ChatRevit"
python .\proxy_mcp_logger.py --listen 127.0.0.1:5221 --upstream http://127.0.0.1:5209 --logdir .\logs
````

Smoke test:

````powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5221/teach/start?name=proxy_smoke" -Method Post
Get-Content .\logs\$(Get-Date -Format yyyy-MM-dd)_mcp.jsonl -Tail 5
````

Note: The proxy sets `Content-Length` explicitly for client compatibility.

---

## 5) Send JSON‑RPC to Revit via the Chain

Two patterns are supported:

1) Fixed forward (Playbook `--forward` points to 5210):

- Client → `POST http://127.0.0.1:5221/rpc` (Playbook forwards to 5210)

2) Per‑request targeting (recommended for multiple Revit instances):

- Client → `POST http://127.0.0.1:5221/t/5211/rpc` (targets Revit at 5211)

Example body (JSON‑RPC smoke test):

````json
{"jsonrpc":"2.0","id":1,"method":"smoke_test","params":{"method":"get_open_documents"}}
````

Example PowerShell:

````powershell
$body = @{ jsonrpc = "2.0"; id = 1; method = "smoke_test"; params = @{ method = "get_open_documents" } } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "http://127.0.0.1:5221/t/5211/rpc" -Method Post -ContentType "application/json" -Body $body
````

Expected: a queued response with `howTo.poll` for results (the add‑in dequeues and posts results).

---

## 6) Caching: Avoid Re‑querying Heavy Models

For frequently used read‑only commands like `get_project_info` and `get_open_documents`, cache results to JSON and reuse them to reduce load on Revit.

- Cache script:

````powershell
python "C:\Users\okuno\Documents\VS2022\Ver431\Codex\Manuals\Scripts\cache_revit_info.py" --proxy http://127.0.0.1:5221 --revit-port 5211 --out-dir "C:\Users\okuno\Documents\VS2022\Ver431\Codex\Manuals\Logs" --ttl-sec 0
````

- Use cached data (concise summary):

````powershell
powershell -File "C:\Users\okuno\Documents\VS2022\Ver431\Codex\Manuals\Scripts\get_project_and_documents_cached.ps1" -Port 5211 -Proxy http://127.0.0.1:5221 -OutDir "C:\Users\okuno\Documents\VS2022\Ver431\Codex\Manuals\Logs"
````

- Force refresh when needed:

````powershell
powershell -File "...\get_project_and_documents_cached.ps1" -Port 5211 -Refresh
````

Outputs are saved under `Codex/Work/<ProjectName>_<Port>/Logs` as `project_info_<port>.json` and `open_documents_<port>.json`.

---

## 7) Troubleshooting

- WinError 10013 / AddressInUse: port conflict. Keep proxy on 5221+, Playbook on 5209, and Revit on 5210/5211/5212.
- Request loops: do NOT set Playbook `--forward` to the proxy. Always forward Playbook to a Revit port. For multi‑instance routing, use `/t/{port}/rpc`.
- No console output: if background started, logs go to files or a separate window. Use foreground runs for direct console output.
- Verify listeners:

  ````powershell
  Get-NetTCPConnection -LocalPort 5209,5210,5211,5212,5221 -State Listen
  ````

---

## 8) One‑Shot Startup (Copy & Paste)

1) Start Revit normally (binds 5210+).

2) Start Playbook (forward to 5210):

````powershell
dotnet run --project "C:\Users\okuno\Documents\VS2022\Ver431\McpPlaybookServer\src\McpPlaybookServer" -- --port 5209 --forward http://127.0.0.1:5210
````

3) Start Proxy (listen 5221):

````powershell
Set-Location "C:\Users\okuno\Documents\VS2022\Ver431\ChatRevit"
python .\proxy_mcp_logger.py --listen 127.0.0.1:5221 --upstream http://127.0.0.1:5209 --logdir .\logs
````

4) Send RPC to a specific Revit instance (example: 5211):

````powershell
$body = @{ jsonrpc = "2.0"; id = 1; method = "smoke_test"; params = @{ method = "get_open_documents" } } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "http://127.0.0.1:5221/t/5211/rpc" -Method Post -ContentType "application/json" -Body $body
````

---

## 9) Where Things Are

- Playbook server code: `McpPlaybookServer/src/McpPlaybookServer`  
  Design/runbook: `McpPlaybookServer/MCP-Playbook-Server_Design_and_Runbook.md`
- Proxy script: `ChatRevit/proxy_mcp_logger.py`  
  Logs: `ChatRevit/logs/YYYY-MM-DD_mcp.jsonl`
- Command references: `Codex/Manuals/ConnectionGuide/*`  
  Quickstarts: `Codex/Manuals/ConnectionGuide/QUICKSTART.md`,  
  Send instructions: `Codex/Manuals/ConnectionGuide/Codex_to_RevitMCP_Command_Send_Instructions_EN.md`

---

## 10) Clean Shutdown

Close the windows you launched, or from PowerShell:

````powershell
Get-NetTCPConnection -LocalPort 5209,5221 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
````

