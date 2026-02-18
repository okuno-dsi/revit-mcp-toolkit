param(
  [Parameter(Mandatory=$false)][string]$ScriptPath,
  [string]$StagingRoot1 = 'C:\CadJobs\Staging',
  [string]$StagingRoot2 = 'C:\Temp\CadJobs\Staging'
)

function Get-LatestScriptPath([string[]]$roots){
  $files = @()
  foreach($r in $roots){ if(Test-Path $r){ $files += Get-ChildItem -Path $r -Recurse -Filter *.scr -File -ErrorAction SilentlyContinue } }
  if(-not $files){ throw "No .scr files under: $($roots -join ', ')" }
  return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

if([string]::IsNullOrWhiteSpace($ScriptPath)){
  $ScriptPath = Get-LatestScriptPath @($StagingRoot1,$StagingRoot2)
}
if(-not (Test-Path $ScriptPath)){ throw "Not found: $ScriptPath" }

$content = Get-Content -Raw -Path $ScriptPath -Encoding UTF8

# Normalize layer commands: merge-> _MERGE, add empty to _NEW
$content = $content -replace '\\(command\\s+\"_\\.-LAYER\"\\s+\"merge\"', '(command "_.-LAYER" "_MERGE"'
$content = $content -replace '\\(command\\s+\"_\\.-LAYER\"\\s+\"_NEW\"\\s+\"([^\"]+)\"\\)', '(command "_.-LAYER" "_NEW" "$1" "")'

$lines = $content -split "`r?`n"
$out = New-Object System.Collections.Generic.List[string]
for($i=0;$i -lt $lines.Count;$i++){
  $line = $lines[$i]
  if($line -match '^\\(command\\s+\"_\\.-LAYER\"\\s+\"_MERGE\"\\s+\"[^\"]+\"\\s+\"([^\"]+)\"'){
    $dst = $matches[1]
    $out.Add(('(command "_.-LAYER" "_THAW" "{0}" "_UNLOCK" "{0}" "")' -f $dst))
  }
  if($line -match '^\\(command\\s+\"_\\.-LAYER\"\\s+\"_MERGE\"' -and $line -notmatch '\"Y\"\\s+\"\"\\)\\s*$'){
    if($line -match '\"Y\"\\)\\s*$'){ $line = $line.TrimEnd(')') + ' "")' }
    elseif($line -match '\\)\\s*$'){ $line = $line.TrimEnd(')') + ' "Y" "")' }
  }
  $out.Add($line)
}

Set-Content -Path $ScriptPath -Value ($out -join "`r`n") -Encoding UTF8
Write-Host "Patched: $ScriptPath"
