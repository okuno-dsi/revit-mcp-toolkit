param([int]$Port = 5215)
Set-Location $PSScriptRoot
$env:ASPNETCORE_URLS = "http://localhost:$Port"
dotnet run
