Param(
  [int]$RevitPort = 5210,
  [string]$RhinoUrl = "http://127.0.0.1:5200"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'

function Write-Info($msg) { Write-Host "[INFO] $msg" }
function Write-Warn($msg) { Write-Warning $msg }

function Test-RevitPort([int]$Port) {
  try {
    $r = Test-NetConnection -ComputerName 'localhost' -Port $Port -WarningAction SilentlyContinue
    return [bool]$r.TcpTestSucceeded
  } catch {
    return $false
  }
}

function Get-Prop($obj, [string]$name) {
  if ($null -eq $obj) { return $null }
  $p = $obj.PSObject.Properties | Where-Object { $_.Name -eq $name }
  if ($p) { return $p.Value } else { return $null }
}

function Get-ResultLeaf($obj) {
  $cur = $obj
  for ($i = 0; $i -lt 4; $i++) {
    $next = Get-Prop $cur 'result'
    if ($null -ne $next) { $cur = $next } else { break }
  }
  return $cur
}

function Resolve-RepoPath([string]$rel) {
  $root = (Get-Location).Path
  return (Resolve-Path (Join-Path $root $rel)).Path
}

function Invoke-RevitJsonRpc([string]$Method, $Params) {
  $py = 'python'
  $scriptPath = Resolve-RepoPath 'Ver342TEST\Codex\send_revit_command.py'
  if (-not (Test-Path $scriptPath)) { throw "Revit client script not found: $scriptPath" }
  $tmp = Join-Path $env:TEMP ("revit_" + $Method + "_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + ".json")
  $args = @('-X','utf8', $scriptPath, '--port', $RevitPort, '--command', $Method, '--output-file', $tmp)
  if ($Params -and ($Params | Get-Member -Name Count -ErrorAction SilentlyContinue)) {
    $paramsPath = Join-Path $env:TEMP ("revit_params_" + $Method + "_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + ".json")
    $Params | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -Path $paramsPath
    $args += @('--params-file', $paramsPath)
  }
  $stdout = [System.IO.Path]::GetTempFileName()
  $stderr = [System.IO.Path]::GetTempFileName()
  $proc = Start-Process -FilePath $py -ArgumentList $args -NoNewWindow -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
  if ($proc.ExitCode -ne 0) {
    $errBody = ''
    if (Test-Path $tmp) { $errBody = (Get-Content $tmp -Raw) }
    $stderrBody = (Get-Content $stderr -Raw -ErrorAction SilentlyContinue)
    throw "Revit command failed: $Method; $stderrBody; $errBody"
  }
  return (Get-Content $tmp -Raw | ConvertFrom-Json)
}

function Test-RhinoServer([string]$BaseUrl) {
  try {
    $probe = @{ jsonrpc='2.0'; id=1; method='__unknown__'; params=@{} } | ConvertTo-Json -Compress
    $u = ($BaseUrl.TrimEnd('/') + '/rpc')
    $resp = Invoke-WebRequest -Method POST -Uri $u -Body $probe -ContentType 'application/json; charset=utf-8' -TimeoutSec 5 -UseBasicParsing
    return $true
  } catch { return $false }
}

function Ensure-RhinoServer([string]$BaseUrl) {
  if (Test-RhinoServer $BaseUrl) { return }
  $starter = Resolve-RepoPath 'RhinoMCP\scripts\start_server.ps1'
  if (Test-Path $starter) {
    Write-Info "Starting RhinoMcpServer at $BaseUrl"
    & $starter -Url $BaseUrl -Config 'Debug' | Out-Null
    Start-Sleep -Seconds 2
  } else {
    Write-Warn "RhinoMcpServer starter not found: $starter"
  }
}

try {
  Write-Info "Checking Revit MCP on port $RevitPort"
  if (-not (Test-RevitPort $RevitPort)) {
    throw "Revit MCP is not reachable on port $RevitPort (Test-NetConnection failed)."
  }

  Write-Info "Ensuring RhinoMcpServer is up at $RhinoUrl"
  Ensure-RhinoServer -BaseUrl $RhinoUrl
  if (-not (Test-RhinoServer $RhinoUrl)) {
    Write-Warn "RhinoMcpServer did not respond on $RhinoUrl. Proceeding; import may fail if Rhino plugin/server is not running."
  }

  Write-Info "Querying selected element IDs from Revit"
  $sel = Invoke-RevitJsonRpc -Method 'get_selected_element_ids' -Params @{}
  $ids = @()
  $leaf = Get-ResultLeaf $sel
  $okProp = Get-Prop $leaf 'ok'
  $resIds = Get-Prop $leaf 'elementIds'
  if ($null -ne $resIds) { $ids = @($resIds) }
  if (-not $ids -or $ids.Count -eq 0) {
    if ($okProp -eq $false) {
      throw ("Revit MCP returned error for get_selected_element_ids: " + ($leaf | ConvertTo-Json -Depth 6 -Compress))
    }
    throw ("Unexpected response for get_selected_element_ids: " + ($sel | ConvertTo-Json -Depth 6 -Compress))
  }
  if (-not $ids -or $ids.Count -eq 0) {
    Write-Info "No elements selected in Revit. Nothing to import."
    exit 0
  }

  Write-Info ("Selected elementIds: " + ($ids -join ','))
  Write-Info "Resolving UniqueIds via get_element_info"
  $info = Invoke-RevitJsonRpc -Method 'get_element_info' -Params @{ elementIds = $ids; rich = $true }
  $uids = @()
  $infoLeaf = Get-ResultLeaf $info
  $elements = Get-Prop $infoLeaf 'elements'
  if ($elements) {
    foreach ($el in $elements) {
      $uid = Get-Prop $el 'uniqueId'
      if ($uid) { $uids += [string]$uid }
    }
  }
  if ($uids.Count -eq 0) { throw "Could not resolve UniqueIds from get_element_info." }
  Write-Info ("UniqueIds: " + ($uids -join ','))

  Write-Info "Calling RhinoMCP rhino_import_by_ids"
  $body = @{ jsonrpc='2.0'; id=[Int64][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); method='rhino_import_by_ids'; params=@{ uniqueIds=$uids; revitBaseUrl=("http://127.0.0.1:{0}" -f $RevitPort) } } | ConvertTo-Json -Depth 7 -Compress
  $rpcUrl = $RhinoUrl.TrimEnd('/') + '/rpc'
  $resp = Invoke-WebRequest -Method POST -Uri $rpcUrl -Body $body -ContentType 'application/json; charset=utf-8' -UseBasicParsing
  $content = $resp.Content | ConvertFrom-Json
  Write-Host ($content | ConvertTo-Json -Depth 8)
  $leaf = Get-ResultLeaf $content
  $ok = Get-Prop $leaf 'ok'
  $imported = Get-Prop $leaf 'imported'
  $errors = Get-Prop $leaf 'errors'
  if ($ok -eq $true -and $imported -gt 0) {
    Write-Info "Import completed: imported=$imported errors=$errors"
  }
  else {
    Write-Warn "rhino_import_by_ids reported errors: imported=$imported errors=$errors. Falling back to bbox snapshots via plugin IPC."
    # Fallback: build bbox mesh from get_element_info(rich=true) and send rhino_import_snapshot directly to plugin IPC
    $info = Invoke-RevitJsonRpc -Method 'get_element_info' -Params @{ uniqueIds = $uids; rich = $true }
    $infoLeaf = Get-ResultLeaf $info
    $elements = Get-Prop $infoLeaf 'elements'
    if (-not $elements) { throw "Fallback failed: could not get elements for bbox." }
    foreach ($el in $elements) {
      $uid = Get-Prop $el 'uniqueId'
      $bbox = Get-Prop $el 'bboxMm'
      if (-not $uid -or -not $bbox) { continue }
      $min = Get-Prop $bbox 'min'; $max = Get-Prop $bbox 'max'
      if (-not $min -or -not $max) { continue }
      $mm_to_ft = 1.0/304.8
      $minx = $min.x * $mm_to_ft; $miny = $min.y * $mm_to_ft; $minz = $min.z * $mm_to_ft
      $maxx = $max.x * $mm_to_ft; $maxy = $max.y * $mm_to_ft; $maxz = $max.z * $mm_to_ft
      $verts = @(@($minx,$miny,$minz), @($maxx,$miny,$minz), @($maxx,$maxy,$minz), @($minx,$maxy,$minz), @($minx,$miny,$maxz), @($maxx,$miny,$maxz), @($maxx,$maxy,$maxz), @($minx,$maxy,$maxz))
      $idx = @(0,1,2,0,2,3,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7)
      $snap = @{ jsonrpc='2.0'; id=[Int64][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); method='rhino_import_snapshot'; params=@{ uniqueId=$uid; units='feet'; vertices=$verts; submeshes=@(@{ materialKey='bbox'; intIndices=$idx }); snapshotStamp=(Get-Date).ToString('o') } } | ConvertTo-Json -Depth 8 -Compress
      $ipcUrl = 'http://127.0.0.1:5201/rpc'
      try {
        $imp = Invoke-WebRequest -Method POST -Uri $ipcUrl -Body $snap -ContentType 'application/json; charset=utf-8' -UseBasicParsing | Select-Object -ExpandProperty Content | ConvertFrom-Json
        $resNode = Get-Prop $imp 'result'
        $okImp = Get-Prop $resNode 'ok'
        if ($okImp -eq $true) {
          Write-Info ("BBox imported for " + $uid)
        } else {
          Write-Warn ("BBox import failed for " + $uid + "; retrying with unique suffix")
          $uid2 = $uid + "-bbox-" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
          $snap2 = @{ jsonrpc='2.0'; id=[Int64][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); method='rhino_import_snapshot'; params=@{ uniqueId=$uid2; units='feet'; vertices=$verts; submeshes=@(@{ materialKey='bbox'; intIndices=$idx }); snapshotStamp=(Get-Date).ToString('o') } } | ConvertTo-Json -Depth 8 -Compress
          $imp2 = Invoke-WebRequest -Method POST -Uri $ipcUrl -Body $snap2 -ContentType 'application/json; charset=utf-8' -UseBasicParsing | Select-Object -ExpandProperty Content | ConvertFrom-Json
          $okImp2 = Get-Prop (Get-Prop $imp2 'result') 'ok'
          if ($okImp2 -eq $true) { Write-Info ("BBox imported for " + $uid2) } else { Write-Warn ("BBox import failed for " + $uid2) }
        }
      } catch { Write-Warn ("IPC failed for " + $uid + " : " + $_) }
    }
  }
}
catch {
  Write-Error $_
  exit 1
}
