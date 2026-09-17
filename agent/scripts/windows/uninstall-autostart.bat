@echo off
title Remove Centrix Autostart
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk"
echo [DONE] Centrix autostart removed. If the agent is still running, restart the PC once.
pause
