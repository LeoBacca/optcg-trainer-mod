@echo off
rem Installs OPTCG Trainer for OPTCGSim (https://github.com/LeoBacca/optcg-trainer-mod).
rem Double-click this file: if the zip files aren't next to it, it downloads the latest version from optcgtrainer.com.
setlocal
if exist "%~dp0files\install.ps1" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0files\install.ps1"
  goto :done
)
set "OPTCG_CMD=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& ([ScriptBlock]::Create(((Get-Content -LiteralPath $env:OPTCG_CMD -Raw) -replace '(?s)^.*?# --- PS ---\r?\n','')))"
:done
if errorlevel 1 (
  echo.
  echo   Something went wrong. Take a screenshot of this window and send it to Leo.
  echo   If the file %TEMP%\optcgtrainer-install.log is there, send it too.
  echo.
  pause
  exit /b 1
)
exit /b 0

# --- PS ---
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# the site keeps a copy of the newest release (and checks it against GitHub): the installer talks only to optcgtrainer.com
$url = 'https://optcgtrainer.com/api/mod/package'
$tmp = Join-Path $env:TEMP ('optcgtrainer-dl-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

try {
    Write-Host ''
    Write-Host '  OPTCG Trainer: downloading the latest version from optcgtrainer.com...'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'package.zip'
    $pkg = Join-Path $tmp 'pkg'
    Invoke-WebRequest -Uri $url -OutFile $zip -Headers @{ 'User-Agent' = 'OPTCG-Trainer-Installer' } -TimeoutSec 120 -UseBasicParsing

    Write-Host '  Extracting...'
    Expand-Archive -LiteralPath $zip -DestinationPath $pkg -Force

    $installer = Join-Path $pkg 'files\install.ps1'
    if (-not (Test-Path -LiteralPath $installer)) { throw 'The downloaded package does not contain files\install.ps1.' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
    exit $LASTEXITCODE
}
catch {
    $err = $_
    $msg = $err.Exception.Message
    if ($err.Exception -is [System.Net.WebException]) {
        if ($err.Exception.Response) {
            # the site's own words (e.g. "try again in a minute") are in the response body, not in the exception
            try { $body = (New-Object IO.StreamReader($err.Exception.Response.GetResponseStream())).ReadToEnd().Trim() } catch { $body = '' }
            if ($body -and $body.Length -lt 300) { $msg = $body }
        }
        else { $msg = "optcgtrainer.com didn't answer ($msg). Check your internet connection and try again in a few minutes." }
    }
    Write-Host ''
    Write-Host "  ERROR: $msg"
    exit 1
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue }
}
