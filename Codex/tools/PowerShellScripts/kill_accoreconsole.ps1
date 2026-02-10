# @feature: kill accoreconsole | keywords: misc
param(
  [switch]$ListOnly,
  [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
  $procs = Get-Process -Name accoreconsole -ErrorAction SilentlyContinue
} catch { $procs = @() }

if(-not $procs -or $procs.Count -eq 0){
  if(-not $Quiet){ Write-Output 'No accoreconsole.exe processes found.' }
  exit 0
}

if(-not $Quiet){
  $procs | Select-Object Id, ProcessName, StartTime, Path | Format-Table -AutoSize | Out-String | Write-Output
}

if($ListOnly){ exit 0 }

try {
  $pids = $procs.Id
  Stop-Process -Id $pids -Force -ErrorAction Stop
  if(-not $Quiet){ Write-Output ("Killed: " + ($pids -join ', ')) }
  exit 0
} catch {
  if(-not $Quiet){ Write-Output ("Error killing accoreconsole: " + $_) }
  exit 1
}


