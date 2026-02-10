param(
  [int]$Port = 5210,
  [switch]$DryRun,
  [int]$MaxTypesPage = 2000,
  [int]$IdsLimit = 200000
)

$ErrorActionPreference = 'Stop'
try { chcp 65001 > $null } catch {}
$env:PYTHONUTF8 = '1'
try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$SCRIPT_DIR = $PSScriptRoot
$REPO_ROOT = Resolve-Path (Join-Path $SCRIPT_DIR '..\..')
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

# Allow env override when -Port is not explicitly provided
if(-not $PSBoundParameters.ContainsKey('Port') -and $env:REVIT_MCP_PORT){
  try { $Port = [int]$env:REVIT_MCP_PORT; Write-Host "[Port] Using REVIT_MCP_PORT=$Port" -ForegroundColor DarkCyan } catch {}
}

function Invoke-RevitCommandJson {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [hashtable]$Params = @{},
    [int]$Port = 5210,
    [int]$JobTimeoutSec = 0
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Depth 20 -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @(
      $PY,
      '--port', $Port,
      '--command', $Method,
      '--params', $paramsJson,
      '--output-file', $tmp.FullName
    )
    if($JobTimeoutSec -gt 0){ $argsList += @('--timeout-sec', $JobTimeoutSec) }
    python @argsList | Out-Null
    $j = Get-Content -Raw -LiteralPath $tmp.FullName -Encoding UTF8 | ConvertFrom-Json
    # unwrap result shapes
    if($j -and $j.result -and $j.result.result){ return $j.result.result }
    if($j -and $j.result){ return $j.result }
    return $j
  } finally {
    Remove-Item -ErrorAction SilentlyContinue $tmp.FullName
  }
}

function Ensure-Dir([string]$Path){ if(-not (Test-Path $Path)){ New-Item -ItemType Directory -Path $Path -Force | Out-Null } }

function Sanitize-Name([string]$Name){ return ($Name -replace '[\\/:*?""<>|]', '_').Trim() }

function Ensure-ProjectWorkDirs([int]$Port, $ProjectInfo){
  $projName = $null
  try { $projName = [string]$ProjectInfo.projectName } catch {}
  if([string]::IsNullOrWhiteSpace($projName)){ $projName = "Project_$Port" }
  $projNameSafe = Sanitize-Name $projName
  $root = Join-Path $REPO_ROOT 'Work'
  Ensure-Dir $root
  $projDir = Join-Path $root ("{0}_{1}" -f $projNameSafe, $Port)
  Ensure-Dir $projDir
  $logs = Join-Path $projDir 'Logs'
  $temp = Join-Path $projDir 'Temp'
  $data = Join-Path $projDir 'Data'
  Ensure-Dir $logs; Ensure-Dir $temp; Ensure-Dir $data
  return @{ Root=$projDir; Logs=$logs; Temp=$temp; Data=$data; ProjectName=$projNameSafe }
}

function Get-RandomShuffle([object[]]$Items){
  # Fisher-Yates
  $arr = @($Items)
  $n = $arr.Count
  for($i=$n-1; $i -gt 0; $i--){
    $j = Get-Random -Minimum 0 -Maximum ($i+1)
    if($j -ne $i){ $t = $arr[$i]; $arr[$i] = $arr[$j]; $arr[$j] = $t }
  }
  return ,$arr
}

Write-Host "[1/6] Get project info" -ForegroundColor Cyan
$projRes = Invoke-RevitCommandJson -Method 'get_project_info' -Params @{} -Port $Port
if(-not $projRes){ throw 'get_project_info returned no payload' }

Write-Host "[2/6] Ensure Projects/<Project>_<Port> folders" -ForegroundColor Cyan
$dirs = Ensure-ProjectWorkDirs -Port $Port -ProjectInfo $projRes
$projInfoPath = Join-Path $dirs.Logs ("project_info_{0}.json" -f $Port)
@{ ok=$true; method='get_project_info'; port=$Port; result=$projRes } | ConvertTo-Json -Depth 32 | Out-File -LiteralPath $projInfoPath -Encoding utf8
Write-Host ("  -> {0}" -f $dirs.Root) -ForegroundColor DarkGreen

Write-Host "[3/6] Resolve active view and walls in view" -ForegroundColor Cyan
$viewRes = Invoke-RevitCommandJson -Method 'get_current_view' -Params @{} -Port $Port
try { $viewId = [int]$viewRes.viewId } catch { $viewId = 0 }
if($viewId -le 0){ throw "Could not resolve active viewId (got '$viewId')" }

# Ask only for Wall instances in the current view
$filter = @{ includeClasses = @('Wall') }
$shape = @{ idsOnly = $true; page = @{ limit = $IdsLimit } }
$geiv = Invoke-RevitCommandJson -Method 'get_elements_in_view' -Params @{ viewId = $viewId; _filter = $filter; _shape = $shape } -Port $Port
$wallIds = @()
if($geiv -and $geiv.elementIds){ $wallIds = @($geiv.elementIds) }
$wallsJsonPath = Join-Path $dirs.Logs ("walls_in_view_{0}.json" -f $Port)
@{ ok=$true; viewId=$viewId; count=$wallIds.Count; elementIds=$wallIds } | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $wallsJsonPath -Encoding utf8
Write-Host ("  -> Walls in view: {0}" -f $wallIds.Count) -ForegroundColor DarkCyan
if($wallIds.Count -eq 0){ Write-Warning 'No walls in the active view. Nothing to do.'; return }

Write-Host "[4/6] Load wall types and ensure enough unique types" -ForegroundColor Cyan
$typesRes = Invoke-RevitCommandJson -Method 'get_wall_types' -Params @{ skip=0; count=$MaxTypesPage } -Port $Port
$types = @($typesRes.types)
if(-not $types -or $types.Count -eq 0){ throw 'No wall types returned from get_wall_types' }

# Normalize to PSCustomObject with typeId and typeName
$typeRows = foreach($t in $types){ if($t -and $t.PSObject.Properties.Name -contains 'typeId'){ [PSCustomObject]@{ typeId=[int]$t.typeId; typeName=[string]$t.typeName } } }
$typeRows = @($typeRows | Where-Object { $_ -ne $null })
$need = $wallIds.Count
$have = $typeRows.Count
$created = @()
if($have -lt $need){
  Write-Host ("  -> Not enough types ({0} < {1}); duplicating..." -f $have, $need) -ForegroundColor Yellow
  $i = 1
  while($typeRows.Count -lt $need){
    $base = $typeRows[ ($typeRows.Count + $i) % [math]::Max(1,$have) ]
    $newName = "${($base.typeName)} AutoCopy $i"
    if($DryRun){
      # Simulate a new type id by a negative placeholder
      $simId = -10000 - $i
      $created += [PSCustomObject]@{ typeId=$simId; typeName=$newName; simulated=$true }
      $typeRows += [PSCustomObject]@{ typeId=$simId; typeName=$newName }
    } else {
      try {
        $dup = Invoke-RevitCommandJson -Method 'duplicate_wall_type' -Params @{ sourceTypeId = $base.typeId; newName = $newName } -Port $Port -JobTimeoutSec 60
        $newId = $null
        try { $newId = [int]$dup.newTypeId } catch { $newId = $null }
        if($null -eq $newId -or $newId -le 0){ throw "duplicate_wall_type returned no newTypeId" }
        $created += [PSCustomObject]@{ typeId=$newId; typeName=$newName; simulated=$false }
        $typeRows += [PSCustomObject]@{ typeId=$newId; typeName=$newName }
      } catch {
        Write-Warning ("duplicate_wall_type failed: {0}" -f $_.Exception.Message)
        break
      }
    }
    $i++
  }
}

Write-Host ("  -> Types available: {0} (created: {1})" -f $typeRows.Count, $created.Count) -ForegroundColor DarkCyan

if($typeRows.Count -lt $wallIds.Count){
  Write-Warning ("Insufficient wall types after duplication: have={0}, need={1}. Proceeding with best-effort unique assignments for the first {0} walls; remaining will be left unchanged." -f $typeRows.Count, $wallIds.Count)
}

Write-Host "[5/6] Build random 1:1 mapping (wall -> unique type)" -ForegroundColor Cyan
$typeRowsShuffled = Get-RandomShuffle -Items $typeRows
# Trim or take up to N types
$targetCount = [math]::Min($typeRowsShuffled.Count, $wallIds.Count)
$assignTypes = @($typeRowsShuffled | Select-Object -First $targetCount)

$mapping = @()
for($k=0; $k -lt $targetCount; $k++){
  $mapping += [PSCustomObject]@{ elementId=[int]$wallIds[$k]; typeId=[int]$assignTypes[$k].typeId; typeName=[string]$assignTypes[$k].typeName }
}
$planPath = Join-Path $dirs.Temp ("unique_wall_types_plan_{0}.json" -f $Port)
@{ ok=$true; count=$mapping.Count; port=$Port; viewId=$viewId; mapping=$mapping } | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $planPath -Encoding utf8
Write-Host ("  -> Planned assignments saved: {0}" -f $planPath) -ForegroundColor DarkGreen

if($DryRun){
  Write-Host "[6/6] DryRun: no changes applied. See mapping plan." -ForegroundColor Yellow
  return
}

Write-Host "[6/6] Apply changes via change_wall_type" -ForegroundColor Cyan
$logFile = Join-Path $dirs.Logs ("unique_wall_types_changes_{0}.jsonl" -f $Port)
if(Test-Path $logFile){ Remove-Item $logFile -Force }
$ok=0; $fail=0
foreach($m in $mapping){
  try {
    $res = Invoke-RevitCommandJson -Method 'change_wall_type' -Params @{ elementId = $m.elementId; typeId = $m.typeId } -Port $Port -JobTimeoutSec 60
    $payload = $res
    $entry = @{ elementId=$m.elementId; targetTypeId=$m.typeId; targetTypeName=$m.typeName; result=$payload }
    ($entry | ConvertTo-Json -Depth 12 -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
    $ok++
  } catch {
    $fail++
    $err = @{ elementId=$m.elementId; targetTypeId=$m.typeId; targetTypeName=$m.typeName; error=$_.Exception.Message }
    ($err | ConvertTo-Json -Compress) | Out-File -LiteralPath $logFile -Append -Encoding utf8
  }
}
Write-Host ("Done. Success={0}, Failed={1}. Log={2}" -f $ok, $fail, $logFile) -ForegroundColor Green

