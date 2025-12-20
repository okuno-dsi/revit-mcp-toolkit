param(
  [string[]]$Inputs,
  [string]$Output,
  [string]$BaseUrl = "http://127.0.0.1:5251",
  [string[]]$Include = @("*"),
  [string[]]$Exclude = @("0","DEFPOINTS"),
  [string]$Format = "{old}_{stem}",
  [int]$TimeoutSec = 15,
  [string]$JobId = $(Get-Date -Format 'yyyyMMddHHmmssfff')
)

if (-not $Inputs -or $Inputs.Count -eq 0) {
  Write-Error "Specify -Inputs (DWG paths)."; exit 1
}
if (-not $Output) { Write-Error "Specify -Output (DWG path)."; exit 1 }

# Build JSON-RPC request
$inObjs = @()
foreach($p in $Inputs){
  $inObjs += @{ path = [IO.Path]::GetFullPath($p); stem = [IO.Path]::GetFileNameWithoutExtension($p) }
}
$body = @{ jsonrpc = "2.0"; id = $JobId; method = "merge_dwgs_perfile_rename"; params = @{ inputs = $inObjs; output = [IO.Path]::GetFullPath($Output); rename = @{ include = $Include; exclude = $Exclude; format = $Format }; mode = "gui" } }

Write-Host "POST /enqueue -> $BaseUrl"
$json = $body | ConvertTo-Json -Depth 6
try {
  $res = Invoke-RestMethod -Method Post -Uri "$BaseUrl/enqueue" -ContentType 'application/json' -Body $json -TimeoutSec $TimeoutSec
  $jid = if ($res.id) { $res.id } else { $JobId }
  Write-Host "Enqueued job: $jid"
  Write-Host "Next: AutoCAD add-in should poll /pending_request and post result."
  Write-Host "To check: GET $BaseUrl/get_result?id=$jid"
} catch {
  Write-Error $_
  exit 1
}

