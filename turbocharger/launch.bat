@echo off
chcp 65001 >nul
title Turbocharger MRP System
cd /d "%~dp0"

taskkill /F /IM dotnet.exe >nul 2>&1
timeout /t 1 /nobreak >nul

start http://localhost:5006
dotnet run --project Turbocharger.csproj

pause