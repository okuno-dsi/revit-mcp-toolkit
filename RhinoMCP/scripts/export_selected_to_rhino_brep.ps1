param(
  [int]$RevitPort = 5210,
  [string]$RhinoUrl = 'http://127.0.0.1:5200',
  [string]$OutDir = "$PSScriptRoot/../tmp_exports"
)

$ErrorActionPreference = 'Stop'

function Invoke-RevitRpc {
  param([string]$Method, [hashtable]$Params, [int]$Port = $RevitPort, [int]$WaitSeconds = 120)
  if (-not $Params) { $Params = @{} }
  $base = "http://127.0.0.1:$Port"
  $call = @{ jsonrpc = '2.0'; id = [int][double]::Parse((Get-Date -UFormat %s) + '000'); method = $Method; params = $Params }
  $body = $call | ConvertTo-Json -Depth 10
  Invoke-RestMethod -UseBasicParsing -Method Post -Uri ($base + '/enqueue?force=1') -ContentType 'application/json; charset=utf-8' -Body $body | Out-Null
  $t0 = Get-Date
  while ($true) {
    try {
      $resp = Invoke-RestMethod -UseBasicParsing -Method Get -Uri ($base + '/get_result') -TimeoutSec 60
      if ($resp) { return $resp }
    } catch {
      # 202/204 handling; keep waiting within timeout
    }
    if ((Get-Date) - $t0 -gt [TimeSpan]::FromSeconds($WaitSeconds)) {
      throw "Timed out waiting for $Method on port $Port"
    }
    Start-Sleep -Milliseconds 200
  }
}

function Get-ResultLeaf([object]$obj) {
  $cur = $obj
  for($i=0;$i -lt 4;$i++){
    if ($cur -is [hashtable] -and $cur.ContainsKey('result') -and $cur.result -is [hashtable]) { $cur = $cur.result } elseif ($cur.PSObject.Properties.Name -contains 'result') { $cur = $cur.result } else { break }
  }
  return $cur
}

Write-Host "Exporting current Revit selection to Rhino (Brep) ..." -ForegroundColor Cyan

# 1) Get selection
$sel = Invoke-RevitRpc -Method 'get_selected_element_ids' -Params @{}
$selLeaf = Get-ResultLeaf $sel
if (-not $selLeaf.elementIds -or $selLeaf.elementIds.Count -eq 0) {
  throw 'No elements are selected in Revit.'
}

# 2) Get current view id
$view = Invoke-RevitRpc -Method 'get_current_view' -Params @{}
$viewLeaf = Get-ResultLeaf $view
if (-not $viewLeaf.viewId) { throw 'Failed to get current view id.' }

# 3) Prepare out path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
$outPath = Join-Path (Resolve-Path $OutDir) "RevitSelection_${stamp}_Brep.3dm"

# 4) Export Brep 3dm
$params = @{
  viewId = [int]$viewLeaf.viewId
  outPath = [string]$outPath
  elementIds = @($selLeaf.elementIds)
  includeLinked = $true
  unitsOut = 'mm'
}
$exp = Invoke-RevitRpc -Method 'export_view_3dm_brep' -Params $params
$expLeaf = Get-ResultLeaf $exp
if (-not $expLeaf.ok) { throw "export_view_3dm_brep failed: $($expLeaf | ConvertTo-Json -Depth 5)" }
if (-not (Test-Path $outPath)) { throw "3dm not found: $outPath" }
Write-Host "Exported: $outPath" -ForegroundColor Green

# 5) Import into Rhino via RhinoMcpServer
$rpc = @{ jsonrpc='2.0'; id=1; method='import_3dm'; params=@{ path=$outPath; autoIndex=$true; units='mm' } } | ConvertTo-Json -Depth 6
$res = Invoke-RestMethod -UseBasicParsing -Method Post -Uri ($RhinoUrl.TrimEnd('/') + '/rpc') -ContentType 'application/json; charset=utf-8' -Body $rpc
$result = $res.result
if (-not $result.ok) { throw ("Rhino import_3dm failed: " + ($result | ConvertTo-Json -Depth 5)) }

Write-Host ("Rhino import ok. Objects=" + $result.objectCount + ", revitLink=" + $result.revitLink) -ForegroundColor Green

