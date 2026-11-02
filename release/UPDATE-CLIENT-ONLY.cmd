@echo off
title Offline DAoC True 1.65 - update the client only
rem Fork helper (Offline DAoC BotRealm): updates only new-client\ from upstream's latest release. The server, the
rem launcher/dashboard and runtime\true165 keep exactly what they have, so no fork build has to be put back.
rem Pass -Update "<folder>" to choose the game folder, or let it find the install itself. For a full update (client,
rem server and content) run GET-OFFLINE-DAOC-TRUE165.cmd -Mode update -AllowLocalChanges instead.
if not exist "%~dp0Get-OfflineDAoC-True165.ps1" (
    echo.
    echo This file belongs next to GET-OFFLINE-DAOC-TRUE165.cmd in the release folder of the fork's repository.
    echo.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-OfflineDAoC-True165.ps1" -Mode update -ClientOnly %*
echo.
pause
