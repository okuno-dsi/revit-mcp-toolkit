param(
  [Parameter(Mandatory=$true)][string]$LeftSnapshot,
  [Parameter(Mandatory=$true)][string]$RightSnapshot,
  [string]$CsvOut = "Work/structural_crossport_differences.csv",
  [string]$Suffix = "相違",
  [int]$BatchSize = 100,
  [int]$WaitSeconds = 300,
  [int]$TimeoutSec = 900,
  [double]$PaddingMm = 150.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
$env:PYTHONUTF8='1'
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

function Read-Json([string]$path){
  return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-Payload($obj){
  if($null -eq $obj){ return $null }
  try {
    $hasResult = $false
    try { $hasResult = ($obj.PSObject.Properties.Match('result').Count -gt 0) } catch { $hasResult = $false }
    if($hasResult){
      $lvl1 = $obj.PSObject.Properties['result'].Value
      $hasResult2 = $false
      try { $hasResult2 = ($lvl1.PSObject.Properties.Match('result').Count -gt 0) } catch { $hasResult2 = $false }
      if($hasResult2){ return $lvl1.PSObject.Properties['result'].Value }
      return $lvl1
    }
  } catch {}
  return $obj
}

function Invoke-Mcp([int]$Port,[string]$Method,[hashtable]$Params,[int]$Wait=[int]$WaitSeconds,[int]$JobSec=[int]$TimeoutSec,[switch]$Force){
  $SCRIPT_DIR = $PSScriptRoot
  $PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'
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

function Norm-Element($e){
  $id = $null; try { $id = [int]($e.elementId) } catch {}
  if(-not $id){ try { $id = [int]($e.id) } catch {} }
  $catId = $null
  try { $catId = [int]($e.categoryId) } catch {}
  if(-not $catId){ try { $catId = [int]($e.category.id) } catch {} }
  $fam = ''
  try { $fam = [string]$e.familyName } catch {}
  if([string]::IsNullOrWhiteSpace($fam)){
    try { $fam = [string]$e.type.familyName } catch {}
  }
  $typ = ''
  try { $typ = [string]$e.typeName } catch {}
  if([string]::IsNullOrWhiteSpace($typ)){
    try { $typ = [string]$e.type.typeName } catch {}
  }
  return [pscustomobject]@{ elementId=$id; categoryId=$catId; familyName=$fam; typeName=$typ }
}

function Group-ByKey($elems){
  $map = @{}
  foreach($x in $elems){
    $k = "{0}|{1}|{2}" -f $x.categoryId, $x.familyName, $x.typeName
    if(-not $map.ContainsKey($k)){ $map[$k] = New-Object System.Collections.ArrayList }
    [void]$map[$k].Add($x)
  }
  return $map
}

function Extract-Elements($snap){
  $els = @()
  try { if($snap.PSObject.Properties.Match('elements').Count -gt 0){ $els = @($snap.elements) } } catch {}
  if($els.Count -eq 0){
    try {
      if($snap.PSObject.Properties.Match('result').Count -gt 0){
        $r1 = $snap.result
        if($r1.PSObject.Properties.Match('elements').Count -gt 0){ $els = @($r1.elements) }
        elseif($r1.PSObject.Properties.Match('result').Count -gt 0 -and $r1.result.PSObject.Properties.Match('elements').Count -gt 0){ $els = @($r1.result.elements) }
      }
    } catch {}
  }
  $norm = @()
  foreach($e in $els){ $n = Norm-Element $e; if($n.elementId){ $norm += $n } }
  return $norm
}

function Ensure-RevisionId([int]$Port){
  $lr = Get-Payload (Invoke-Mcp $Port 'list_revisions' @{} 120 240 -Force)
  $items = @(); try { $items = @($lr.revisions) } catch {}
  if($items.Count -gt 0){
    try { return [int]$items[-1].id } catch {}
  }
  $cr = Get-Payload (Invoke-Mcp $Port 'create_default_revision' @{} 60 120 -Force)
  try { $rid = [int]$cr.revisionId; if($rid -gt 0){ return $rid } } catch {}
  # re-list
  $lr2 = Get-Payload (Invoke-Mcp $Port 'list_revisions' @{} 120 240 -Force)
  $items2 = @(); try { $items2 = @($lr2.revisions) } catch {}
  if($items2.Count -gt 0){ try { return [int]$items2[-1].id } catch {} }
  return 0
}

function Duplicate-ToDiffView([int]$Port,[int]$ViewId,[string]$Suffix){
  # Try direct duplicate_view with desiredName first; fallback to helper script
  try {
    # base name
    $vi = Get-Payload (Invoke-Mcp $Port 'get_view_info' @{ viewId=$ViewId } 60 120 -Force)
    $baseName = [string]$vi.view.name
    if([string]::IsNullOrWhiteSpace($baseName)){ $baseName = [string]$vi.name }
    if([string]::IsNullOrWhiteSpace($baseName)){ $baseName = "View_$ViewId" }
    $desired = ($baseName + ' ' + $Suffix)
    $idm = ("crossport:{0}:{1}" -f $ViewId, $desired)
    $dp = @{ viewId=$ViewId; desiredName=$desired; onNameConflict='returnExisting'; idempotencyKey=$idm; withDetailing=$true }
    $res = Get-Payload (Invoke-Mcp $Port 'duplicate_view' $dp 180 360 -Force)
    $nv = 0
    try { $nv = [int]$res.viewId } catch {}
    if($nv -le 0){ try { $nv = [int]$res.newViewId } catch {} }
    if($nv -gt 0){
      try { $null = Invoke-Mcp $Port 'activate_view' @{ viewId=$nv } 30 60 -Force } catch {}
      # Ensure name has suffix (some views may ignore desiredName)
      try {
        $vi2 = Get-Payload (Invoke-Mcp $Port 'get_view_info' @{ viewId=$nv } 60 120 -Force)
        $nm = [string]$vi2.view.name; if([string]::IsNullOrWhiteSpace($nm)){ $nm = [string]$vi2.name }
        if($nm -and -not $nm.EndsWith(" $Suffix")){
          $newNm = ($nm + ' ' + $Suffix)
          try { $null = Invoke-Mcp $Port 'rename_view' @{ viewId=$nv; newName=$newNm } 60 120 -Force } catch {}
        }
      } catch {}
      return $nv
    }
  } catch {}
  # fallback to helper script (rename path)
  try { $null = Invoke-Mcp $Port 'activate_view' @{ viewId = $ViewId } 60 120 -Force } catch {}
  $dupScript = Join-Path $PSScriptRoot 'duplicate_active_view_safe.ps1'
  $raw = & pwsh -NoProfile -File $dupScript -Port $Port -Suffix $Suffix -OnConflict 'useExisting' -WithDetailing -ActivateNew 2>$null
  try { $obj = $raw | ConvertFrom-Json -Depth 50 } catch { $obj = $null }
  if($obj -and $obj.newViewId){
    $nv = [int]$obj.newViewId
    try {
      $vi3 = Get-Payload (Invoke-Mcp $Port 'get_view_info' @{ viewId=$nv } 60 120 -Force)
      $nm3 = [string]$vi3.view.name; if([string]::IsNullOrWhiteSpace($nm3)){ $nm3 = [string]$vi3.name }
      if($nm3 -and -not $nm3.EndsWith(" $Suffix")){
        $newNm3 = ($nm3 + ' ' + $Suffix)
        try { $null = Invoke-Mcp $Port 'rename_view' @{ viewId=$nv; newName=$newNm3 } 60 120 -Force } catch {}
      }
    } catch {}
    return $nv
  }
  # fallback to current active
  $cv = Get-Payload (Invoke-Mcp $Port 'get_current_view' @{} 30 60 -Force)
  try { return [int]$cv.viewId } catch { return 0 }
}

function Cloud-Elements([int]$Port,[int]$ViewId,[int[]]$ElementIds,[int]$RevId,[double]$Padding){
  if(-not $ElementIds -or $ElementIds.Count -eq 0){ return }
  foreach($eid in $ElementIds){
    $params = @{ viewId = $ViewId; elementId = ([int]$eid); paddingMm = $Padding; preZoom='element'; restoreZoom=$false; focusMarginMm=150; mode='aabb' }
    if($RevId -gt 0){ $params['revisionId'] = $RevId }
    try { $null = Invoke-Mcp $Port 'create_revision_cloud_for_element_projection' $params 120 480 -Force } catch {
      # retry once without revisionId
      $params.Remove('revisionId')
      try { $null = Invoke-Mcp $Port 'create_revision_cloud_for_element_projection' $params 120 480 -Force } catch {}
    }
  }
}

# Load snapshots
$leftRaw = Read-Json -path $LeftSnapshot
$rightRaw = Read-Json -path $RightSnapshot
$left = Get-Payload $leftRaw
$right = Get-Payload $rightRaw

if(-not $left -or -not $right){ throw 'Failed to parse snapshots.' }

$leftPort = 0; try { $leftPort = [int]$left.port } catch {}
$rightPort = 0; try { $rightPort = [int]$right.port } catch {}
$leftView = 0; try { $leftView = [int]$left.viewId } catch {}
$rightView = 0; try { $rightView = [int]$right.viewId } catch {}
$leftProj = $left.project
$rightProj = $right.project

$leftElems = Extract-Elements $left
$rightElems = Extract-Elements $right

$GLeft = Group-ByKey $leftElems
$GRight = Group-ByKey $rightElems

$keys = New-Object System.Collections.Generic.HashSet[string]
foreach($k in $GLeft.Keys){ [void]$keys.Add($k) }
foreach($k in $GRight.Keys){ [void]$keys.Add($k) }

$flagLeft = @()
$flagRight = @()
$rows = @()

foreach($k in $keys){
  $leftList = @()
  if($GLeft.ContainsKey($k)){
    foreach($it in $GLeft[$k]){ $leftList += ,$it }
  }
  $rightList = @()
  if($GRight.ContainsKey($k)){
    foreach($it in $GRight[$k]){ $rightList += ,$it }
  }
  $lc = @($leftList).Count
  $rc = @($rightList).Count
  if($lc -eq $rc){ continue }
  # parse key
  $parts = $k.Split('|',3)
  $cat = $parts[0]
  $fam = $parts[1]
  $typ = $parts[2]
  $note = "count mismatch; left=$lc right=$rc"
  if($lc -gt $rc){
    $take = $lc - $rc
    $flag = @($leftList | Select-Object -First $take)
    $flagLeft += $flag
    foreach($e in $flag){ $rows += [pscustomobject]@{ port=$leftPort; project=($leftProj.name); viewId=$leftView; elementId=$e.elementId; categoryId=[int]$cat; familyName=$fam; typeName=$typ; side='left'; note=$note } }
  } else {
    $take = $rc - $lc
    $flag = @($rightList | Select-Object -First $take)
    $flagRight += $flag
    foreach($e in $flag){ $rows += [pscustomobject]@{ port=$rightPort; project=($rightProj.name); viewId=$rightView; elementId=$e.elementId; categoryId=[int]$cat; familyName=$fam; typeName=$typ; side='right'; note=$note } }
  }
}

# Duplicate views and cloud
$leftNewView = 0
$rightNewView = 0
if($flagLeft.Count -gt 0 -and $leftPort -gt 0 -and $leftView -gt 0){
  $leftNewView = Duplicate-ToDiffView -Port $leftPort -ViewId $leftView -Suffix $Suffix
  $revL = Ensure-RevisionId -Port $leftPort
  Cloud-Elements -Port $leftPort -ViewId $leftNewView -ElementIds (@($flagLeft | ForEach-Object { $_.elementId })) -RevId $revL -Padding $PaddingMm
}
if($flagRight.Count -gt 0 -and $rightPort -gt 0 -and $rightView -gt 0){
  $rightNewView = Duplicate-ToDiffView -Port $rightPort -ViewId $rightView -Suffix $Suffix
  $revR = Ensure-RevisionId -Port $rightPort
  Cloud-Elements -Port $rightPort -ViewId $rightNewView -ElementIds (@($flagRight | ForEach-Object { $_.elementId })) -RevId $revR -Padding $PaddingMm
}

# Write CSV
$csvPath = Resolve-Path (Join-Path (Resolve-Path .).Path $CsvOut) -ErrorAction SilentlyContinue
if(-not $csvPath){ $csvPath = (Join-Path (Resolve-Path .).Path $CsvOut) }
$dir = [System.IO.Path]::GetDirectoryName([string]$csvPath)
if(-not (Test-Path $dir)){ New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$rows | Export-Csv -LiteralPath $csvPath -Encoding UTF8 -NoTypeInformation

$summary = [pscustomobject]@{
  ok = $true
  leftPort = $leftPort
  rightPort = $rightPort
  leftViewId = $leftView
  rightViewId = $rightView
  leftNewViewId = $leftNewView
  rightNewViewId = $rightNewView
  leftFlagged = $flagLeft.Count
  rightFlagged = $flagRight.Count
  csv = [string]$csvPath
}
$summary | ConvertTo-Json -Depth 6
