@echo off
title Centrix Agent
cd /d "%~dp0"

echo =======================================================
echo     Centrix (self-contained)
echo =======================================================
echo.

:: First run? Run the setup wizard (asks folder/center/room, saves config)
if not exist "%~dp0.configured" (
    echo First run detected - opening the setup wizard...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
)

echo Dashboard (this PC):              http://localhost:5200/
echo Dashboard (phone/MaxHub, WiFi):   http://^(PC-IP^):5200/   ^(IP: see DASHBOARD-ACCESS.txt^)
echo API key: see DASHBOARD-ACCESS.txt
echo If the agent stops, it restarts itself in 5 seconds.
echo To stop completely: close this window.
echo.

:loop
if exist "%~dp0CentrixAgent.exe" (
    "%~dp0CentrixAgent.exe"
) else if exist "%~dp0Centrix.exe" (
    "%~dp0Centrix.exe"
) else (
    "%~dp0LectureAgent.exe"
)
echo.
echo Agent stopped. Restarting in 5 seconds... (close this window to stop completely)
timeout /t 5 /nobreak >nul
goto loop
