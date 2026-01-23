Param(
  [Parameter(Mandatory=$true)][string]$SeedDwg,
  [Parameter(Mandatory=$true)][string]$ScriptPath,
  [Parameter(Mandatory=$false)][string]$OutDwg,
  [string]$AccorePath = 'C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe',
  [string]$Locale = 'ja-JP',
  [int]$TimeoutSec = 180,
  [int]$StableSeconds = 2,
  [int]$PollIntervalSec = 1
)

$ErrorActionPreference = 'Stop'
if(-not (Test-Path $SeedDwg)){ throw "Seed DWG not found: $SeedDwg" }
if(-not (Test-Path $ScriptPath)){ throw "Script not found: $ScriptPath" }
if(-not (Test-Path $AccorePath)){ throw "accoreconsole not found: $AccorePath" }

# Derive OutDwg from script name when not provided (best-effort)
if([string]::IsNullOrWhiteSpace($OutDwg)){
  $OutDwg = [IO.Path]::ChangeExtension($ScriptPath, '.out.dwg')
}
$outAbs = (Resolve-Path (New-Item -ItemType File -Force -Path $OutDwg)).Path
Remove-Item $outAbs -Force -ErrorAction SilentlyContinue

$outDir = [IO.Path]::GetDirectoryName($outAbs)
$base = [IO.Path]::Combine($outDir, [IO.Path]::GetFileNameWithoutExtension($outAbs))
$logOut = "$base.accore.stdout.log"
$logErr = "$base.accore.stderr.log"
try{ Remove-Item $logOut,$logErr -Force -ErrorAction SilentlyContinue }catch{}

# Launch accoreconsole
$args = @('/i', $SeedDwg, '/s', $ScriptPath, '/l', $Locale)
$p = Start-Process -FilePath $AccorePath -ArgumentList $args -WorkingDirectory ([IO.Path]::GetDirectoryName($SeedDwg)) -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru

# Monitor: output file stable-size heuristic
$deadline = (Get-Date).AddSeconds([Math]::Max(5,$TimeoutSec))
$lastSize = -1L; $stableStart = $null; $completedByFile = $false
while(-not $p.HasExited){
  Start-Sleep -Seconds ([Math]::Max(1,$PollIntervalSec))
  if(Test-Path $outAbs){
    try{ $size = (Get-Item $outAbs).Length }catch{ $size = -1 }
    if($size -gt 0){
      if($size -eq $lastSize){
        if(-not $stableStart){ $stableStart = Get-Date }
        elseif(((Get-Date) - $stableStart).TotalSeconds -ge [Math]::Max(1,$StableSeconds)){
          $completedByFile = $true; break
        }
      } else { $lastSize = $size; $stableStart = $null }
    }
  }
  if((Get-Date) -gt $deadline){ break }
}

if($completedByFile){
  if(-not $p.HasExited){ $null = $p.WaitForExit(10000) }
  if(-not $p.HasExited){ try{ $p.Kill($true) }catch{} }
  Write-Host ("OK: {0}" -f $outAbs) -ForegroundColor Green
} else {
  if(-not $p.HasExited){ try{ $p.Kill($true) }catch{} }
  Write-Warning "accoreconsole timeout after ${TimeoutSec}s (no stable output)"
  if(Test-Path $logOut){ Write-Host '--- stdout tail ---' -ForegroundColor DarkGray; Get-Content -Tail 120 -Path $logOut }
  if(Test-Path $logErr){ Write-Host '--- stderr tail ---' -ForegroundColor DarkGray; Get-Content -Tail 120 -Path $logErr }
  throw "accoreconsole timeout"
}

