@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%diagnose_revitmcp_5210.ps1"

if not exist "%PS1%" (
  echo [ERROR] Script not found:
  echo %PS1%
  pause
  exit /b 1
)

echo Running RevitMCP diagnostics...
echo - Port: 5210 (fixed)
echo - Mode: Full (fixed)
echo - Output: this folder
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -OpenOutputFolder
set "RC=%ERRORLEVEL%"

echo.
echo Finished. ExitCode=%RC%
echo Check diagnostics_*.json and diagnostics_*.md in:
echo %SCRIPT_DIR%
echo.
pause
exit /b 0

