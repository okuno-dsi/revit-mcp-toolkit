@echo off
REM ================================================================
REM show_revitmcpserver.bat
REM  List running RevitMCPServer.exe processes
REM ================================================================

echo Listing RevitMCPServer.exe processes...

tasklist /FI "IMAGENAME eq RevitMCPServer.exe" | find /I "RevitMCPServer.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo No RevitMCPServer.exe processes found.
) else (
    tasklist /FI "IMAGENAME eq RevitMCPServer.exe"
)

pause
