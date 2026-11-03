@echo off
title BotRealm - downloader
if not exist "%~dp0Get-BotRealm.ps1" (
    echo.
    echo This file was started from inside the zip. Right-click the downloaded zip, choose
    echo "Extract All...", open the extracted folder, and double-click this file there.
    echo.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-BotRealm.ps1" %*
echo.
pause
