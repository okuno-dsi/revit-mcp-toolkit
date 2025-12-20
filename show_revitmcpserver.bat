@echo off
REM ================================================================

tasklist /FI "IMAGENAME eq RevitMCPServer.exe" | find /I "RevitMCPServer.exe" 

pause