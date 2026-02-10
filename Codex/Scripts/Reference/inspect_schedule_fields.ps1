param(
  [int]$Port = 5210,
  [string]$Title,
  [int]$ScheduleViewId,
  [int]$SamplePerField = 5
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

function Resolve-ScheduleId {
  param([int]$Port,[string]$Title,[int]$ScheduleViewId)
  if ($ScheduleViewId) { return $ScheduleViewId }
  if (-not $Title) { throw "Specify -Title or -ScheduleViewId" }
  $sched = Invoke-RevitCommandJson -Method 'get_schedules' -Params @{} -Port $Port
  if (-not $sched -or -not $sched.ok) { throw "get_schedules failed" }
  $hit = @($sched.schedules | Where-Object { $_.title -eq $Title }) | Select-Object -First 1
  if (-not $hit) { throw "Schedule not found: $Title" }
  return [int]$hit.scheduleViewId
}

function Inspect-Schedule {
  param([int]$Port,[int]$Id,[int]$SamplePerField)
  $data = Invoke-RevitCommandJson -Method 'get_schedule_data' -Params @{ scheduleViewId = $Id } -Port $Port
  if (-not $data -or -not $data.ok -or -not $data.rows) { throw "get_schedule_data returned no rows." }
  $rows = @($data.rows)
  if ($rows.Count -lt 1) { throw "No rows found." }
  # Assume first row is mapping: internalFieldName -> displayLabel
  $mapRow = $rows[0]
  $map = @{}
  foreach ($p in $mapRow.PSObject.Properties) { $map[$p.Name] = [string]$p.Value }
  # Sample values per display label from subsequent rows
  $samples = @{}
  for ($i=1; $i -lt $rows.Count; $i++) {
    $r = $rows[$i]
    foreach ($k in $map.Keys) {
      $label = $map[$k]
      if ([string]::IsNullOrWhiteSpace($label)) { continue }
      $v = $null
      if ($r.PSObject.Properties[$label]) { $v = [string]$r.$label }
      if (-not [string]::IsNullOrWhiteSpace($v)) {
        if (-not $samples.ContainsKey($label)) { $samples[$label] = New-Object System.Collections.Generic.List[string] }
        if (-not ($samples[$label].Contains($v))) {
          $samples[$label].Add($v) | Out-Null
        }
      }
    }
  }
  # Emit report
  Write-Host "DisplayName, FieldName, Samples" -ForegroundColor Cyan
  foreach ($k in $map.Keys) {
    $label = $map[$k]
    if ([string]::IsNullOrWhiteSpace($label)) { continue }
    $vals = @()
    if ($samples.ContainsKey($label)) { $vals = $samples[$label] | Select-Object -First $SamplePerField }
    $joined = ($vals -join ' | ')
    Write-Host ("{0}, {1}, {2}" -f $label, $k, $joined)
  }
}

$id = Resolve-ScheduleId -Port $Port -Title $Title -ScheduleViewId $ScheduleViewId
Inspect-Schedule -Port $Port -Id $id -SamplePerField $SamplePerField




