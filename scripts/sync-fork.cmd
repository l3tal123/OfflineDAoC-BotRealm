@echo off
rem Double-clickable wrapper for scripts\sync-upstream.ps1.
rem Fetches shadowofze/OfflineDAoC-True165 main, merges it into this fork's main,
rem and pushes to origin (l3tal123/OfflineDAoC-BotRealm). Never force-pushes.
rem Usage:  scripts\sync-fork.cmd [-DryRun] [-NoPush]
setlocal
set "SCRIPT=%~dp0sync-upstream.ps1"
if not exist "%SCRIPT%" (
    echo ERROR: %SCRIPT% not found.
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
