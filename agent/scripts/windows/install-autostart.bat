@echo off
title Install Centrix Autostart
cd /d "%~dp0"

set "SCRIPT_DIR=%~dp0"
set "TARGET=%SCRIPT_DIR%start-silent.vbs"
set "SHORTCUT_PATH=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Centrix.lnk"

powershell -NoProfile -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%SHORTCUT_PATH%'); $Shortcut.TargetPath = '%TARGET%'; $Shortcut.WorkingDirectory = '%SCRIPT_DIR%'; $Shortcut.Description = 'Centrix Background Service'; $Shortcut.Save();"

if %errorlevel% equ 0 (
    echo [SUCCESS] Centrix added to Windows Startup.
    echo - The agent starts hidden in the background whenever this PC boots.
    echo - If it ever stops, a watchdog restarts it within 5 seconds.
    echo To remove it later: uninstall-autostart.bat
) else (
    echo [ERROR] Failed to create the startup shortcut.
)
echo.
pause
