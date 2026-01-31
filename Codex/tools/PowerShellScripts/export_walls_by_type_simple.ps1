# @feature: export walls by type simple | keywords: 壁, スペース, ビュー, DWG
param(
  [int]$Port = 5210,
  [string]$ProjectDir,
  [int]$MaxWaitSec = 300,
  [int]$JobTimeoutSec = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8 = '1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

$ROOT = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$WORKROOT = Join-Path $ROOT 'Codex\Work'
if ([string]::IsNullOrWhiteSpace($ProjectDir)){
  $proj = Get-ChildItem -LiteralPath $WORKROOT -Directory -Filter 'Project_*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if(-not $proj){ throw "No project folder under $WORKROOT. Specify -ProjectDir." }
  $ProjectDir = $proj.FullName
}
if(-not (Test-Path $ProjectDir)){ throw "ProjectDir not found: $ProjectDir" }
$OutDir = Join-Path $ProjectDir 'DWG'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$LogsDir = Join-Path $ProjectDir 'Logs'
New-Item -ItemType Directory -Path $LogsDir -Force | Out-Null

$PY = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Invoke-Mcp {
  param([string]$Method,[hashtable]$Params,[int]$Wait=$MaxWaitSec,[int]$JobSec=$JobTimeoutSec,[switch]$Force)
  $pjson = ($Params | ConvertTo-Json -Depth 100 -Compress)
  $args = @('--port', $Port, '--command', $Method, '--params', $pjson, '--wait-seconds', [string]$Wait)
  if($JobSec -gt 0){ $args += @('--timeout-sec', [string]$JobSec) }
  if($Force){ $args += '--force' }
  $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ("mcp_"+[System.IO.Path]::GetRandomFileName()+".json"))
  $args += @('--output-file', $tmp)
  $null = & python -X utf8 $PY @args 2>$null
  $code = $LASTEXITCODE
  $txt = ''
  try { $txt = Get-Content -LiteralPath $tmp -Raw -Encoding UTF8 } catch {}
  if(Test-Path $tmp){ Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
  if($code -ne 0){ throw "MCP call failed ($Method): $txt" }
  if([string]::IsNullOrWhiteSpace($txt)){ throw "Empty response from MCP ($Method)" }
  return ($txt | ConvertFrom-Json -Depth 400)
}

function Get-ActiveViewId {
  $cv = Invoke-Mcp 'get_current_view' @{} 60 120 -Force
  foreach($p in 'result.result.viewId','result.viewId','viewId'){
    try{ $cur=$cv; foreach($seg in $p.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return [int]$cur }catch{}
  }
  throw 'Could not resolve active viewId'
}

function Get-IdsInView([int]$viewId,[int[]]$includeCatIds,[int[]]$excludeCatIds){
  $shape = @{ idsOnly = $true; page = @{ limit = 200000 } }
  $filter = @{}
  if($includeCatIds){ $filter['includeCategoryIds'] = @($includeCatIds) }
  if($excludeCatIds){ $filter['excludeCategoryIds'] = @($excludeCatIds) }
  $params = @{ viewId=$viewId; _shape=$shape; _filter=$filter }
  $res = Invoke-Mcp 'get_elements_in_view' $params 300 300 -Force
  foreach($path in 'result.result.elementIds','result.elementIds','elementIds'){
    try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; return @($cur | ForEach-Object { [int]$_ }) }catch{}
  }
  return @()
}

function Get-ElementInfoBulk([int[]]$ids){
  $rows = @()
  if(-not $ids -or $ids.Count -eq 0){ return $rows }
  $chunk = 200
  for($i=0; $i -lt $ids.Count; $i+=$chunk){
    $batch = @($ids[$i..([Math]::Min($i+$chunk-1,$ids.Count-1))])
    $res = Invoke-Mcp 'get_element_info' @{ elementIds=$batch; rich=$false } 300 300 -Force
    foreach($path in 'result.result.elements','result.elements','elements'){
      try{ $cur=$res; foreach($seg in $path.Split('.')){ $cur=$cur | Select-Object -ExpandProperty $seg -ErrorAction Stop }; foreach($e in $cur){ $rows += $e }; break }catch{}
    }
  }
  return $rows
}

function SanitizeStem([string]$s){ if([string]::IsNullOrWhiteSpace($s)){ return 'UNKNOWN' }; return ($s -replace '[^A-Za-z0-9_-]','_') }

$viewId = Get-ActiveViewId
Write-Host ("[View] id={0}" -f $viewId) -ForegroundColor Cyan

$WALL = -2000011
$wallIds    = Get-IdsInView -viewId $viewId -includeCatIds @($WALL) -excludeCatIds @()
$nonwallIds = Get-IdsInView -viewId $viewId -includeCatIds @() -excludeCatIds @($WALL)
Write-Host ("[Counts] walls={0} nonwalls={1}" -f $wallIds.Count, $nonwallIds.Count)

# 1) seed.dwg = 非壁のみ（要素ID指定）
$outAbs = (Resolve-Path $OutDir).Path.Replace('\\','/')
$seed = Invoke-Mcp 'export_dwg' @{ viewId=$viewId; outputFolder=$outAbs; fileName='seed'; dwgVersion='ACAD2018'; elementIds=@($nonwallIds); __smoke_ok=$true } $MaxWaitSec $JobTimeoutSec -Force
($seed | ConvertTo-Json -Depth 30) | Out-File -FilePath (Join-Path $LogsDir 'export_seed_simple.json') -Encoding utf8

# 2) 壁タイプごとに書き出し
$info = Get-ElementInfoBulk -ids $wallIds
$byType = @{}
foreach($e in $info){
  $t=''; try{ $t=[string]$e.typeName }catch{}
  $stem = SanitizeStem $t
  if(-not $byType.ContainsKey($stem)){ $byType[$stem] = New-Object System.Collections.Generic.List[int] }
  try{ [void]$byType[$stem].Add([int]$e.elementId) }catch{}
}

foreach($stem in $byType.Keys){
  $ids = @($byType[$stem] | ForEach-Object { [int]$_ })
  Write-Host ("[Export] walls_{0}.dwg ({1} ids)" -f $stem, $ids.Count) -ForegroundColor Green
  $res = Invoke-Mcp 'export_dwg' @{ viewId=$viewId; outputFolder=$outAbs; fileName=("walls_"+$stem); dwgVersion='ACAD2018'; elementIds=@($ids); __smoke_ok=$true } $MaxWaitSec $JobTimeoutSec -Force
  ($res | ConvertTo-Json -Depth 30) | Out-File -FilePath (Join-Path $LogsDir ("export_walls_"+$stem+".json")) -Encoding utf8
}

Write-Host 'DWG export (seed + per-type) complete.' -ForegroundColor Green

