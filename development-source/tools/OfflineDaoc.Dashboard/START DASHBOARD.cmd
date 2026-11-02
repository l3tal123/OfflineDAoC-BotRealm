@echo off
rem Offline DAoC BotRealm - web dashboard (preview).
rem Everything this needs lives in this folder: delete the folder to remove the feature.
title Offline DAoC BotRealm - dashboard
cd /d "%~dp0"
"%~dp0OfflineDaoc.Dashboard.exe" %*
if errorlevel 2 pause
