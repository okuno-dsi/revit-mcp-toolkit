@echo off
setlocal
set "ROOT=%~dp0"
set "PS1=%ROOT%Scripts\diagnose_revitmcp_5210.ps1"

if not exist "%PS1%" (
  echo [ERROR] Script not found:
  echo %PS1%
  pause
  exit /b 1
)

echo Running RevitMCP diagnostics...
echo - Port: 5210 (fixed)
echo - Mode: Full (fixed)
echo - Output: %ROOT%Output
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -OpenOutputFolder
set "RC=%ERRORLEVEL%"

echo.
echo Finished. ExitCode=%RC%
echo Check diagnostics_*.json and diagnostics_*.md in:
echo %ROOT%Output
echo.
pause
exit /b %RC%
