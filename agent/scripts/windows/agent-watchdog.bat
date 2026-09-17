@echo off
title Centrix Watchdog (background)
cd /d "%~dp0"

:: Silent watchdog: keeps the agent running 24x7 and restarts it if it ever stops.
:loop
if exist "CentrixAgent.exe" (
    "CentrixAgent.exe"
) else if exist "Centrix.exe" (
    "Centrix.exe"
) else (
    "LectureAgent.exe"
)
timeout /t 5 /nobreak >nul
goto loop
