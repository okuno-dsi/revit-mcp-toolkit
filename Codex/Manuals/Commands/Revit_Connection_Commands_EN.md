# Revit Connection Commands (EN)

Copy‑paste snippets for Windows PowerShell (pwsh) to bring up the full chain and verify.

## Verify Listener Ports

````powershell
Get-NetTCPConnection -LocalPort 5209,5210,5211,5212 -State Listen |
  Select-Object LocalPort,State,OwningProcess,@{N='Name';E={(Get-Process -Id $_.OwningProcess).ProcessName}}
````

## Start Playbook (port 5209 → Revit 5210)

````powershell
dotnet run --project "C:\Users\okuno\Documents\VS2022\Ver431\McpPlaybookServer\src\McpPlaybookServer" -- --port 5209 --forward http://127.0.0.1:5210
````

## Playbook Health Check

````powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5209/teach/start?name=health" -Method Post
````

## Send JSON‑RPC to Specific Revit Instance (e.g., 5211)

````powershell
$body = @{ jsonrpc = "2.0"; id = 1; method = "smoke_test"; params = @{ method = "get_open_documents" } } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "http://127.0.0.1:5209/t/5211/rpc" -Method Post -ContentType "application/json" -Body $body
````

## Stop Playbook

````powershell
Get-NetTCPConnection -LocalPort 5209 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
````

