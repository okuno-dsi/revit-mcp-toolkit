param(
  [string]$Configuration = "Release",
  [string]$OutputDir = $(Join-Path $PSScriptRoot "publish\Release")
)

$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
  Write-Host "[1/4] dotnet clean"
  dotnet clean .\ExcelMCP.sln -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed." }

  Write-Host "[2/4] dotnet build"
  dotnet build .\ExcelMCP.sln -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

  Write-Host "[3/4] dotnet test"
  dotnet test .\ExcelMCP.sln -c $Configuration --no-build
  if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

  if (Test-Path $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

  Write-Host "[4/4] dotnet publish -> $OutputDir"
  dotnet publish .\ExcelMCP.csproj -c $Configuration -o $OutputDir --no-build
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

  foreach ($file in @('mcp_commands.jsonl', 'README.md', 'MANUAL_JA.md', 'BUILD_RELEASE.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $OutputDir $file) -Force
  }

  Write-Host "Publish completed: $OutputDir"
}
finally {
  Pop-Location
}
