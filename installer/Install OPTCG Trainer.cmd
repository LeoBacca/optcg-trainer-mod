@echo off
rem Installs OPTCG Trainer for OPTCGSim. Read files\install.ps1 to see exactly what it does.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0files\install.ps1"
if errorlevel 1 pause
