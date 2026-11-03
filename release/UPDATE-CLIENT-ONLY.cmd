@echo off
title BotRealm - update the client only
rem Fork helper (BotRealm): updates only new-client\ from upstream's latest release. The server, the
rem launcher/dashboard and runtime\botrealm keep exactly what they have, so no fork build has to be put back.
rem Pass -Update "<folder>" to choose the game folder, or let it find the install itself. For a full update (client,
rem server and content) run GET-BOTREALM.cmd -Mode update -AllowLocalChanges instead.
if not exist "%~dp0Get-BotRealm.ps1" (
    echo.
    echo This file belongs next to GET-BOTREALM.cmd in the release folder of the fork's repository.
    echo.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-BotRealm.ps1" -Mode update -ClientOnly %*
echo.
pause
