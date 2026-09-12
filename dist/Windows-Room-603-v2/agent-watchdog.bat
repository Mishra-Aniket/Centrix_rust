@echo off
title LectureAgent Watchdog (background)
cd /d "%~dp0"

:: Silent watchdog: keeps the agent running 24x7 and restarts it if it ever stops.
:loop
LectureAgent.exe
timeout /t 5 /nobreak >nul
goto loop
