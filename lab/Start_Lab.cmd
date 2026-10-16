@echo off
rem Starts the Leak Hunt Lab server (real app, traffic monitor, connection monitor) and opens the lab.
cd /d "%~dp0"
where node >nul 2>&1
if errorlevel 1 (
  echo Node.js was not found. Install Node.js LTS, then run this file again.
  pause
  exit /b 1
)
if not exist "..\LeakHunt.App\bin\Release" (
  echo First run: building the app. This takes about a minute...
  dotnet build "..\LeakHunt.App" -c Release -v q
  dotnet build "..\EgressTrafficMonitor" -c Release -v q
)
netstat -ano | findstr ":8770" | findstr "LISTENING" >nul
if errorlevel 1 (
  start "Leak Hunt Lab server" /min node server.js
  echo Starting the lab server...
  ping -n 5 127.0.0.1 >nul
) else (
  echo The lab server is already running.
)
start "" "http://127.0.0.1:8770/"
echo.
echo Lab is open at http://127.0.0.1:8770/
echo Leave the minimized "Leak Hunt Lab server" window open during the talk. Run Stop_Lab.cmd to stop.
echo.
ping -n 4 127.0.0.1 >nul

