@echo off
REM ================================================================
REM kill_revitmcpserver.bat
REM  RevitMCPServer.exe をすべて強制終了するバッチ
REM ================================================================

echo Stopping all RevitMCPServer.exe processes...

REM プロセスが存在するかチェック
tasklist /FI "IMAGENAME eq RevitMCPServer.exe" | find /I "RevitMCPServer.exe" >nul
if %ERRORLEVEL% neq 0 (
    echo No RevitMCPServer.exe processes found.
    goto :eof
)

REM 強制終了 (/F)、子プロセスも合わせて終了 (/T)
taskkill /F /IM RevitMCPServer.exe /T

if %ERRORLEVEL% equ 0 (
    echo All RevitMCPServer.exe processes terminated.
) else (
    echo Failed to terminate some processes.
)
