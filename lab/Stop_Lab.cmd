@echo off
if exist "%~dp0data\wifi_off.flag" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wifi.ps1" -Action on >nul 2>&1
if exist "%~dp0data\wifi_off.flag" del "%~dp0data\wifi_off.flag"
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :8770 ^| findstr LISTENING') do taskkill /PID %%a /T /F >nul 2>&1
taskkill /IM EgressTrafficMonitor.exe /F >nul 2>&1
taskkill /IM LeakHunt.App.exe /F >nul 2>&1
echo Lab stopped.
