Param(
  [string]$Url = "http://127.0.0.1:5200",
  [string]$Config = "Debug"
)

$p = Join-Path (Get-Location) 'PROGRESS.md'
Add-Content $p ("$(Get-Date -Format o) - CMD: start_server.ps1 Url=$Url Config=$Config")

Push-Location RhinoMcpServer
dotnet build -c $Config | Out-Null
Pop-Location

$dll = Resolve-Path "RhinoMcpServer\bin\$Config\net6.0\RhinoMcpServer.dll"
$env:ASPNETCORE_URLS = $Url
$proc = Start-Process -FilePath dotnet -ArgumentList "`"$dll`"" -PassThru -WindowStyle Hidden
$proc.Id | Set-Content server.pid
Write-Output ("SERVER_PID:" + $proc.Id)

