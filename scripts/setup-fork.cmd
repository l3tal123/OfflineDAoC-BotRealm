@echo off
rem Double-clickable wrapper for scripts\bootstrap-fork.ps1.
rem Sets up/repairs this checkout: git remotes (fork + upstream), sync settings, fetches.
rem Safe to run repeatedly; it does not change files, merge, push or build.
setlocal
set "SCRIPT=%~dp0bootstrap-fork.ps1"
if not exist "%SCRIPT%" (
    echo ERROR: %SCRIPT% not found.
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
