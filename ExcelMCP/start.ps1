param(
  [int]$Port = 5215,
  [switch]$Background,
  [switch]$Child,
  [string]$LogDir = "",
  [string]$PidFile = ""
)

$ErrorActionPreference = 'Stop'

function Get-DefaultLogDir {
  Join-Path $env:LOCALAPPDATA 'Revit_MCP\Logs\ExcelMCP'
}

function Get-DefaultPidFile {
  Join-Path $env:LOCALAPPDATA 'Revit_MCP\Run\ExcelMCP.pid'
}

function Test-PidRunning([string]$PidPath) {
  if (-not (Test-Path $PidPath)) { return $false }
  try {
    $json = Get-Content $PidPath -Raw | ConvertFrom-Json
    if (-not $json.pid) { return $false }
    $null = Get-Process -Id ([int]$json.pid) -ErrorAction Stop
    return $true
  } catch {
    return $false
  }
}

if ([string]::IsNullOrWhiteSpace($LogDir)) { $LogDir = Get-DefaultLogDir }
if ([string]::IsNullOrWhiteSpace($PidFile)) { $PidFile = Get-DefaultPidFile }

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($PidFile)) | Out-Null

if ($Background -and -not $Child) {
  if (Test-PidRunning $PidFile) {
    Write-Host "ExcelMCP is already running." -ForegroundColor Yellow
    return
  }

  $argList = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', ('"{0}"' -f $PSCommandPath),
    '-Port', $Port,
    '-Child',
    '-LogDir', ('"{0}"' -f $LogDir),
    '-PidFile', ('"{0}"' -f $PidFile)
  )

  Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -WindowStyle Hidden | Out-Null
  Write-Host "ExcelMCP background start requested. Port=$Port" -ForegroundColor Green
  Write-Host "PID file: $PidFile"
  Write-Host "Logs: $LogDir"
  return
}

Set-Location $PSScriptRoot
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:EXCELMCP_LOG_DIR = $LogDir
$env:EXCELMCP_PID_FILE = $PidFile

dotnet run --project "$PSScriptRoot\ExcelMCP.csproj" --configuration Release --urls "http://localhost:$Port"
