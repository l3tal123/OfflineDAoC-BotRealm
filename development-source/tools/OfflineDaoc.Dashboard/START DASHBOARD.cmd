@echo off
rem BotRealm - launcher and dashboard.
rem Runs the launcher from the build output folder (bin\Release\net10.0).
title BotRealm - launcher
cd /d "%~dp0"
"%~dp0BotRealm.exe" %*
if errorlevel 2 pause
