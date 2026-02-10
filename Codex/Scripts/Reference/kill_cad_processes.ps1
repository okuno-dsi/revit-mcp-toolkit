param(
  [switch]$ListOnly,
  [switch]$Quiet,
  [switch]$IncludeAcad
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CadProcs {
  param([switch]$IncludeAcad)
  $names = @('accoreconsole')
  if($IncludeAcad){ $names += 'acad' }
  $list = @()
  foreach($n in $names){
    try { $p = Get-Process -Name $n -ErrorAction SilentlyContinue } catch { $p = @() }
    if($p){ $list += $p }
  }
  return $list
}

$procs = Get-CadProcs -IncludeAcad:$IncludeAcad
if(-not $procs -or $procs.Count -eq 0){
  if(-not $Quiet){ Write-Output 'No accoreconsole.exe/acad.exe processes found.' }
  exit 0
}

if(-not $Quiet){
  $procs | Select-Object Id, ProcessName, StartTime, Path | Sort-Object ProcessName, StartTime | Format-Table -AutoSize | Out-String | Write-Output
}

if($ListOnly){ exit 0 }

try {
  $pids = $procs.Id
  Stop-Process -Id $pids -Force -ErrorAction Stop
  if(-not $Quiet){ Write-Output ("Killed: " + ($pids -join ', ')) }
  exit 0
} catch {
  if(-not $Quiet){ Write-Output ("Error killing processes: " + $_) }
  exit 1
}

