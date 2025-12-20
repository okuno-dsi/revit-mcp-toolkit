Param(
  [string]$Base = "http://127.0.0.1:5200",
  [string]$Snapshot = "testdata\snapshot_min.json"
)

function Write-Prog($msg) {
  $p = Join-Path (Get-Location) 'PROGRESS.md'
  Add-Content $p ("$(Get-Date -Format o) - CMD: test_rpc.ps1 - " + $msg)
}

function Post-Json([string]$url, [string]$json) {
  return Invoke-WebRequest -Method POST -Uri $url -Body $json -ContentType 'application/json; charset=utf-8' -SkipHttpErrorCheck
}

Write-Prog "healthz"
$h = Invoke-RestMethod -Method GET -Uri "$Base/healthz" -TimeoutSec 5
"HEALTHZ: " + ($h | ConvertTo-Json -Compress)

Write-Prog "unknown_method"
$unk = '{"jsonrpc":"2.0","id":1,"method":"unknown_method","params":{}}'
$r1 = Post-Json "$Base/rpc" $unk
"UNKNOWN: STATUS=" + [int]$r1.StatusCode
$r1.Content

Write-Prog "rhino_import_snapshot"
$snapJson = Get-Content $Snapshot -Raw -Encoding UTF8
$call = @{ jsonrpc = '2.0'; id = 2; method = 'rhino_import_snapshot'; params = (ConvertFrom-Json $snapJson) } | ConvertTo-Json -Depth 6 -Compress
$r2 = Post-Json "$Base/rpc" $call
"IMPORT: STATUS=" + [int]$r2.StatusCode
$r2.Content

Write-Prog "rhino_get_selection"
$sel = '{"jsonrpc":"2.0","id":3,"method":"rhino_get_selection","params":{}}'
$r3 = Post-Json "$Base/rpc" $sel
"SELECTION: STATUS=" + [int]$r3.StatusCode
$r3.Content

Write-Prog "rhino_commit_transform"
$commit = '{"jsonrpc":"2.0","id":4,"method":"rhino_commit_transform","params":{}}'
$r4 = Post-Json "$Base/rpc" $commit
"COMMIT: STATUS=" + [int]$r4.StatusCode
$r4.Content
