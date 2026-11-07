@echo off
rem Removes OPTCG Trainer. Read files\uninstall.ps1 to see exactly what it does.
setlocal
if not exist "%~dp0files\uninstall.ps1" (
  echo.
  echo   OPTCG Trainer: the zip is not extracted yet.
  echo   Right-click the zip file, choose "Extract All...", then double-click "Uninstall OPTCG Trainer" in that folder.
  echo.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0files\uninstall.ps1"
if errorlevel 1 (
  echo.
  echo   Something went wrong. Take a screenshot of this window and send it to Leo.
  echo.
  pause
)
