@echo off
rem Removes OPTCG Trainer. Read files\uninstall.ps1 to see exactly what it does.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0files\uninstall.ps1"
if errorlevel 1 pause
