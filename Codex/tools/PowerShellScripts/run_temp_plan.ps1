# @feature: run temp plan | keywords: 壁, スペース, ビュー, Excel, レベル
param(
  [int]$Port = 5210,
  [string]$BaseLevelName = '1FL',
  [string]$TopLevelName,
  [double]$HeightMm,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tempDir = Join-Path $repoRoot 'temp'
$xlsx = Join-Path $tempDir 'plan.xlsx'
$specJson = Join-Path $tempDir 'excelplan.json'

if(!(Test-Path $xlsx)) { throw "plan.xlsx not found: $xlsx" }
if(!(Test-Path $specJson)) { throw "excelplan.json not found: $specJson" }

$spec = Get-Content $specJson -Raw | ConvertFrom-Json
$method = $spec.cmd
if([string]::IsNullOrWhiteSpace($method)) { throw "No 'cmd' in excelplan.json" }

$params = @{}
$resolved = (Resolve-Path $xlsx).Path
$params.excelPath = $resolved
$params.path = $resolved
$params.levelName = $BaseLevelName
if($PSBoundParameters.ContainsKey('TopLevelName') -and $TopLevelName){ $params.topLevelName = $TopLevelName }
if($PSBoundParameters.ContainsKey('HeightMm') -and $HeightMm -gt 0){ $params.heightMm = [double]$HeightMm }

# optional defaults
$params.cellSizeMeters = 1.0
$params.mode = 'Walls'
$params.wallTypeName = 'RC150'
$params.baseOffsetMm = 0
$params.flip = $false

$outDir = Resolve-Path (Join-Path $PSScriptRoot '..\Logs')
$outFile = Join-Path $outDir ("{0}_{1:yyyyMMdd_HHmmss}.json" -f $method, (Get-Date))

Write-Host ("[Execute] method={0} level={1} excel={2} top={3} heightMm={4}" -f $method,$BaseLevelName,$params.excelPath, ($params.topLevelName|Out-String).Trim(), ($params.heightMm|Out-String).Trim()) -ForegroundColor Cyan

if($DryRun){
  $preview = [pscustomobject]@{ ok=$true; method=$method; port=$Port; params=$params }
  $preview | ConvertTo-Json -Depth 8
  exit 0
}

$py = Join-Path $repoRoot 'Manuals\Scripts\send_revit_command_durable.py'
python $py --port $Port --command $method --params (ConvertTo-Json $params -Depth 10 -Compress) --output-file $outFile --timeout-sec 600 --wait-seconds 240

Write-Host ("[Saved] " + $outFile) -ForegroundColor Green
