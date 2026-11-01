@echo off
rem Double-clickable wrapper for scripts\register-sync-task.ps1.
rem Registers (or removes) the daily Windows Scheduled Task that runs the fork sync.
rem Usage:  scripts\register-sync-task.cmd [-DailyAt 09:00] [-RunNow] [-Remove]
setlocal
set "SCRIPT=%~dp0register-sync-task.ps1"
if not exist "%SCRIPT%" (
    echo ERROR: %SCRIPT% not found.
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
