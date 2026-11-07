@echo off
rem Installs OPTCG Trainer for OPTCGSim. Read files\install.ps1 to see exactly what it does.
setlocal
if not exist "%~dp0files\install.ps1" (
  echo.
  echo   OPTCG Trainer: the zip is not extracted yet.
  echo.
  echo   1. Close this window.
  echo   2. Right-click the zip file and choose "Extract All...", then "Extract".
  echo   3. In the folder that opens, double-click "Install OPTCG Trainer".
  echo.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0files\install.ps1"
if errorlevel 1 (
  echo.
  echo   Something went wrong. Take a screenshot of this window and send it to Leo,
  echo   together with this file: %TEMP%\optcgtrainer-install.log
  echo.
  pause
)
