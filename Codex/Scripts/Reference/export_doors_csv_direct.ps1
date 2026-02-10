param(
  [int]$Port = 5210,
  [string]$OutPath
)

$ErrorActionPreference = 'Stop'

function Invoke-RevitCommandJson {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [hashtable]$Params = @{},
    [int]$Port = 5210
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @(
      "Scripts/Reference/send_revit_command_durable.py",
      "--port", $Port,
      "--command", $Method,
      "--params", $paramsJson,
      "--output-file", $tmp.FullName
    )
    python @argsList | Out-Null
    $j = Get-Content -Raw -LiteralPath $tmp.FullName | ConvertFrom-Json
    return $j.result.result
  } finally {
    Remove-Item -ErrorAction SilentlyContinue $tmp.FullName
  }
}

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $PSScriptRoot '..\\..\\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

function Get-ProjectNameSafe {
  param([int]$Port)
  $logs = Resolve-LogsDir -p $Port
  $cands = @(
    (Join-Path $logs ("project_info_{0}.json" -f $Port)),
    (Join-Path $logs 'project_info.json'),
    (Join-Path 'Manuals/Logs' ("project_info_{0}.json" -f $Port)),
    (Join-Path 'Manuals/Logs' 'project_info.json')
  )
  foreach($path in $cands){
    if(Test-Path $path){
      try{
        $j = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        $name = $j.result.result.projectName
        if($name){ return ($name -replace '[\\/:*?"<>|]', '_') }
      }catch{}
    }
  }
  return ("Project_{0}" -f $Port)
}

# 1) Fetch doors (element + type/basic info)
Write-Host "[1/3] Fetching door instances..." -ForegroundColor Cyan
$doorsRes = Invoke-RevitCommandJson -Method 'get_doors' -Params @{} -Port $Port
if (-not $doorsRes.ok -or -not $doorsRes.doors) { throw "No doors found." }
$doors = @($doorsRes.doors)

# 2) Bulk: instance params (Mark)
Write-Host "[2/3] Fetching instance parameters (Mark)..." -ForegroundColor Cyan
$ids = @($doors | ForEach-Object { [int]$_.elementId })
$paramKeys = @(@{ name = 'マーク' })
$items = @()
for ($i=0; $i -lt $ids.Count; $i+=200) {
  $chunk = $ids[$i..([Math]::Min($i+199, $ids.Count-1))]
  $p = @{ elementIds = $chunk; paramKeys = $paramKeys; page = @{ startIndex = 0; batchSize = 500 } }
  $res = Invoke-RevitCommandJson -Method 'get_instance_parameters_bulk' -Params $p -Port $Port
  if ($res -and $res.ok -and $res.items) { $items += @($res.items) }
}
$markById = @{}
foreach ($it in $items) { $markById[[int]$it.elementId] = $it.params.'マーク' }

# 3) Type parameters cache (width/height)
Write-Host "[2.5/3] Fetching type parameters (幅/高さ)..." -ForegroundColor Cyan
$typeIds = @($doors | Select-Object -ExpandProperty typeId -Unique)
$sizeByType = @{}
$typeNameByType = @{}
foreach ($tid in $typeIds) {
  $resT = Invoke-RevitCommandJson -Method 'get_door_type_parameters' -Params @{ typeId = [int]$tid } -Port $Port
  if ($resT -and $resT.ok -and $resT.parameters) {
    $h = $null; $w = $null; $tn = $null
    foreach ($pp in $resT.parameters) {
      if ($pp.name -eq '高さ') { $h = $pp.value }
      elseif ($pp.name -eq '幅') { $w = $pp.value }
      elseif ($pp.name -eq 'タイプ名') { $tn = $pp.value }
    }
    $sizeByType[[int]$tid] = @{ 高さ = $h; 幅 = $w }
    if (-not [string]::IsNullOrWhiteSpace([string]$tn)) { $typeNameByType[[int]$tid] = $tn }
  }
}

# 4) Compose CSV rows
$proj = Get-ProjectNameSafe -Port $Port
if (-not $OutPath) {
  $dir = Join-Path 'Work' ("{0}_{1}" -f $proj, $Port)
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  $OutPath = Join-Path $dir '集計表_ドア.csv'
}
Write-Host ("[3/3] Writing CSV → {0}" -f $OutPath) -ForegroundColor Cyan

$rows = New-Object System.Collections.Generic.List[object]
foreach ($d in $doors) {
  $tid = [int]$d.typeId
  $size = $sizeByType[$tid]
  $typeOut = $d.typeName
  if ([string]::IsNullOrWhiteSpace([string]$typeOut)) {
    if ($typeNameByType.ContainsKey($tid)) { $typeOut = $typeNameByType[$tid] }
  }
  $rows.Add([pscustomobject]@{
    部屋名   = $d.levelName
    番号     = $markById[[int]$d.elementId]
    開き形状 = ''
    タイプ   = $typeOut
    幅       = if($size){ $size['幅'] } else { $null }
    高さ     = if($size){ $size['高さ'] } else { $null }
    個数     = 1
  }) | Out-Null
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath $OutPath
Write-Host "Done."



