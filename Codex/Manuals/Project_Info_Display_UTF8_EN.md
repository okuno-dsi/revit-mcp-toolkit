# Display Revit Project Info (UTF-8, No Mojibake)

Purpose
- Show how to fetch and display Revit project info without garbled text on Windows (UTF-8 safe), using the durable client.

Prerequisites
- Revit is running and the MCP Add-in is active (default port `5210`).
- PowerShell 5+/7+ and Python 3.x are available.
- Python package `requests` installed: `python -m pip install --user requests`.

Quick (Console Output, UTF-8 Safe)
- Paste the following in PowerShell. Adjust `5210` if needed.

```powershell
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$port = 5210

# Optional: connectivity check
Test-NetConnection localhost -Port $port | Out-Null

# Call durable client
$json = python "Codex/Manuals/Scripts/send_revit_command_durable.py" --port $port --command get_project_info
$obj = $json | ConvertFrom-Json
$inner = if ($obj.result -and $obj.result.result) { $obj.result.result } elseif ($obj.result) { $obj.result } else { $obj }

Write-Host "--- Project Info ---" -ForegroundColor Cyan
Write-Host ("ProjectName: {0}" -f $inner.projectName)
Write-Host ("ProjectNumber: {0}" -f $inner.projectNumber)
Write-Host ("ClientName: {0}" -f $inner.clientName)
Write-Host ("Status: {0}" -f $inner.status)
if ($inner.site -and $inner.site.placeName) { Write-Host ("Site: {0}" -f $inner.site.placeName) }
if ($inner.inputUnits -and $inner.internalUnits) { Write-Host ("Units: input={0} / internal={1}" -f $inner.inputUnits.Length, $inner.internalUnits.Length) }
```

Safer File-Based Flow (Recommended for Logs)
- Avoid shell redirection `>` for JSON (guaranteed mojibake). Let the client write UTF-8 directly.

```powershell
chcp 65001 > $null
$env:PYTHONUTF8 = '1'

$port = 5210
$out = "Codex/Work/Project_$port/Logs/get_project_info_${port}.json"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

python "Codex/Manuals/Scripts/send_revit_command_durable.py" --port $port --command get_project_info --output-file $out

$proj = Get-Content -Raw -Encoding UTF8 -LiteralPath $out | ConvertFrom-Json
$inner = $proj.result.result
"ProjectName: {0}" -f $inner.projectName
```

Using the Cached Helper (Optional)
- The helper prints a concise UTF-8 summary and can output full JSON with `-Full`.

```powershell
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/get_project_and_documents_cached.ps1 -Port 5210
# or
pwsh -ExecutionPolicy Bypass -File Codex/Manuals/Scripts/get_project_and_documents_cached.ps1 -Port 5210 -Full
```

Troubleshooting
- Ensure Revit MCP port is listening: `Test-NetConnection localhost -Port 5210`.
- If `requests` is missing: `python -m pip install --user requests`.
- Use `-ExecutionPolicy Bypass` when running unsigned `*.ps1` scripts.
- Do not use shell `>` redirection for JSON outputs; prefer the client's `--output-file` and read with `-Encoding UTF8`.

References
- Quickstart: `Codex/Manuals/ConnectionGuide/QUICKSTART.md`
- Durable client: `Codex/Manuals/Scripts/send_revit_command_durable.py`
- Execution policy: `Codex/Manuals/ExecutionPolicy_Windows.md`
- Durable vs Legacy flow (includes get_project_info): `Codex/Manuals/Durable_vs_Legacy_Request_Flow.md`

