param(
  [string]$Root = "C:\Users\okuno\Documents\VS2022\Ver431\RevitMCPAddin",
  [string]$OutCsv = "Work/Logs/performance_status_cs.csv"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
chcp 65001 > $null
try { $enc = New-Object System.Text.UTF8Encoding $false; [Console]::OutputEncoding = $enc; $OutputEncoding = $enc } catch {}

# Helpers: robust path normalization (abs/rel, slash, case)
function Normalize-Abs([string]$path){
  if([string]::IsNullOrWhiteSpace($path)){ return "" }
  $p = $path.Trim() -replace '/', '\\'
  try { $full = [System.IO.Path]::GetFullPath($p) } catch { $full = $p }
  return $full.ToLowerInvariant()
}
function Normalize-Rel([string]$path){
  if([string]::IsNullOrWhiteSpace($path)){ return "" }
  $p = $path.Trim() -replace '\\','/'
  return $p.ToLowerInvariant()
}

# Output/write mode: support dry-run when OutCsv is '-' or $NoWrite is truthy
if (-not (Get-Variable -Name NoWrite -Scope Script -ErrorAction SilentlyContinue)) { $script:NoWrite = $false }
$NoWrite = ([bool]$NoWrite) -or ($OutCsv -eq '-')

if(-not $NoWrite){ if(!(Test-Path (Split-Path -Parent $OutCsv))){ New-Item -ItemType Directory -Path (Split-Path -Parent $OutCsv) -Force | Out-Null } }

# Build modified list from Work/Optimize/done_list.txt when present
$modified = @()
$modifiedRel = @()
try {
  $candidates = @(
    (Join-Path $PSScriptRoot '..\..\Work\Optimize\done_list.txt'),
    'Work\Optimize\done_list.txt'
  )
  $doneFile = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
  if ($doneFile) {
    $lines = Get-Content -LiteralPath $doneFile -Encoding UTF8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($ln in $lines) {
      if ([System.IO.Path]::IsPathRooted($ln)) { $modified += $ln }
      else { $modified += (Join-Path $Root $ln); $modifiedRel += ($ln -replace '\\','/') }
    }
  }
} catch {}

$modifiedAbsSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$modifiedRelSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
try {
  $candidates2 = @(
    (Join-Path $PSScriptRoot '..\\..\\Work\\Optimize\\done_list.txt'),
    'Work\\Optimize\\done_list.txt'
  )
  $doneFile2 = $candidates2 | Where-Object { Test-Path $_ } | Select-Object -First 1
  if ($doneFile2) {
    $lines2 = Get-Content -LiteralPath $doneFile2 -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $rootAbsNorm2 = Normalize-Abs $Root
    foreach ($ln2 in $lines2) {
      if ([System.IO.Path]::IsPathRooted($ln2)) {
        $lnAbs2 = Normalize-Abs $ln2
        [void]$modifiedAbsSet.Add($lnAbs2)
        if ($lnAbs2.StartsWith($rootAbsNorm2 + '\\')) {
          $relFromAbs2 = $lnAbs2.Substring(($rootAbsNorm2 + '\\').Length).Replace('\\','/')
          [void]$modifiedRelSet.Add((Normalize-Rel $relFromAbs2))
        }
      } else {
        [void]$modifiedRelSet.Add((Normalize-Rel $ln2))
        $absFromRel2 = [System.IO.Path]::GetFullPath((Join-Path $Root $ln2))
        [void]$modifiedAbsSet.Add((Normalize-Abs $absFromRel2))
      }
    }
  }
} catch {}

# Optional manual overrides map: Work/Optimize/status_overrides.json
$overrides = @{}
try {
  $ovCandidates = @(
    (Join-Path $PSScriptRoot '..\\..\\Work\\Optimize\\status_overrides.json'),
    'Work\\Optimize\\status_overrides.json'
  )
  $ovFile = $ovCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
  if ($ovFile) {
    $ovRaw = Get-Content -LiteralPath $ovFile -Raw -Encoding UTF8
    $ovObj = $ovRaw | ConvertFrom-Json
    $props = $ovObj.psobject.Properties.Name
    foreach($k in $props){
      $key = (Normalize-Rel $k)
      $overrides[$key] = $ovObj.$k
    }
  }
} catch {}

$heavyNameMatches = @(
  'ShowAllInViewCommand.cs','IsolateByFilterInViewCommand.cs','HideElementsInViewCommand.cs',
  'ExportDwg','ApplyConditionalColoring','SetVisualOverride','ClearVisualOverride',
  'GetElementsInViewCommand.cs','DuplicateViewSimpleCommand.cs',
  'ExportDwgWithWorksetBucketingCommand.cs','ExportDwgByParamGroupsCommand.cs','ExportDwgByParamGroupsTick.cs','LongOpEngine.cs'
)

$files = Get-ChildItem -Recurse -File -Path $Root -Filter '*.cs'
$rows = @()

foreach($f in $files){
  $rel = $f.FullName.Replace($Root+'\','').Replace('\','/')
  $content = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
  $commandName = ''
  $m = [regex]::Match($content, 'CommandName\s*=>\s*"([^"]+)"')
  if($m.Success){ $commandName = $m.Groups[1].Value }

  if($rel -like 'Commands/*'){
    $category = ($rel -split '/')[1]
  } elseif($rel -like 'Core/*'){
    $category = 'Core'
  } elseif($rel -like 'RevitUI/*'){
    $category = 'RevitUI'
  } else {
    $category = ($rel -split '/')[0]
  }

  $isHeavy = $false
  foreach($nm in $heavyNameMatches){ if($rel -like ('*'+$nm+'*')){ $isHeavy=$true; break } }
  if(-not $isHeavy){ if($content -match 'FilteredElementCollector|HideElements\(|SetElementOverrides\(|Regenerate\(|\bExport\(|Duplicate\('){ $isHeavy=$true } }

  # Robust done detection with normalized comparisons
  $fullNorm = Normalize-Abs $f.FullName
  $relNorm = Normalize-Rel $rel
  $done = $modifiedAbsSet.Contains($fullNorm) -or $modifiedRelSet.Contains($relNorm)
  if($isHeavy){ $need = ($done) ? '不要' : '要' } else { $need = '不要' }
  $status = ($done) ? '作業済み' : '未済'

  if($done){
    switch -Regex ($rel){
      'ShowAllInViewCommand' { $note='Slice-resets skipped; id cache; stats' }
      'ViewManagementCommands' { $note='delete_view: skip Regenerate; refresh opt-in' }
      'IsolateByFilterInViewCommand' { $note='Time-sliced; first-slice reset only; id cache' }
      'GetElementsInViewCommand' { $note='Early category filter applied' }
      default { $note='Updated' }
    }
  } elseif($isHeavy){
    $note='Candidate: batching/time-slice/caching/minimize regenerate'
  } else {
    $note=''
  }

  # Apply optional overrides if present
  if ($overrides.ContainsKey($relNorm)) {
    $ov = $overrides[$relNorm]
    try { if ($null -ne $ov.done) { $done = [bool]$ov.done } } catch {}
    try { if ($ov.note) { $note = [string]$ov.note } } catch {}
  }

  $rows += [pscustomobject]@{
    file = $rel
    category = $category
    command_name = $commandName
    performance_sensitive = ($(if($isHeavy){'Yes'}else{'No'}))
    対応要否 = $need
    状態 = $status
    備考 = $note
  }
}

$sorted = $rows | Sort-Object category, file
if(-not $NoWrite){
  $sorted | Export-Csv -Path $OutCsv -Encoding UTF8 -NoTypeInformation
  Write-Host ("Saved: {0}" -f $OutCsv) -ForegroundColor Green
}
else {
  $sorted | ConvertTo-Csv -NoTypeInformation | Write-Output
  Write-Host ("Generated {0} rows (dry-run)" -f ($sorted.Count)) -ForegroundColor Yellow
}

