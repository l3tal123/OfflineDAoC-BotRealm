@echo off
rem Offline DAoC BotRealm - launcher and dashboard.
rem Runs the launcher from the build output folder (bin\Release\net10.0).
title Offline DAoC BotRealm - launcher
cd /d "%~dp0"
"%~dp0OfflineDAoC.exe" %*
if errorlevel 2 pause
