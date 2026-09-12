@echo off
title Remove LectureAgent Autostart
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\LectureAgent.lnk"
echo [DONE] Autostart removed. If the agent is still running, restart the PC once.
pause
