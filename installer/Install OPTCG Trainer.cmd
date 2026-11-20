@echo off
rem Installs OPTCG Trainer for OPTCGSim (https://github.com/LeoBacca/optcg-trainer-mod).
rem Double-click this file: if the zip files aren't next to it, it downloads the latest release from GitHub.
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
  echo   Something went wrong. Take a screenshot of this window and send it to Leo,
  echo   together with this file: %TEMP%\optcgtrainer-install.log
  echo.
  pause
  exit /b 1
)
exit /b 0

# --- PS ---
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$api = 'https://api.github.com/repos/LeoBacca/optcg-trainer-mod/releases?per_page=20'
$allowed = 'https://github.com/LeoBacca/optcg-trainer-mod/releases/download/'
$tmp = Join-Path $env:TEMP ('optcgtrainer-dl-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

try {
    Write-Host ''
    Write-Host '  OPTCG Trainer: checking latest version on GitHub...'
    $headers = @{ 'User-Agent' = 'OPTCG-Trainer-Installer'; 'Accept' = 'application/vnd.github+json' }
    # Windows PowerShell 5.1 hands the JSON array back as ONE object: @(Invoke-RestMethod ...) would hide every release inside it
    $resp = Invoke-RestMethod -Uri $api -Headers $headers -TimeoutSec 20 -UseBasicParsing
    $releases = @($resp)

    $bestVer = $null
    $bestAsset = $null
    $bestTag = $null
    foreach ($r in $releases) {
        if ($r.draft) { continue }
        $raw = (([string]$r.tag_name) -replace '^v', '') -replace '-.*$', ''
        $ver = $null
        if (-not [Version]::TryParse($raw, [ref]$ver)) { continue }
        $asset = @($r.assets) | Where-Object { $_.name -match '^OPTCG-Trainer-[\w.-]+\.zip$' } | Select-Object -First 1
        if (-not $asset) { continue }
        $url = [string]$asset.browser_download_url
        if (-not $url.StartsWith($allowed, [StringComparison]::Ordinal)) { continue }
        if ($null -eq $bestVer -or $ver -gt $bestVer) {
            $bestVer = $ver
            $bestAsset = $asset
            $bestTag = [string]$r.tag_name
        }
    }
    if (-not $bestAsset) { throw 'Could not find a downloadable OPTCG Trainer release on GitHub.' }

    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'package.zip'
    $pkg = Join-Path $tmp 'pkg'

    Write-Host "  Downloading OPTCG Trainer $bestTag..."
    Invoke-WebRequest -Uri $bestAsset.browser_download_url -OutFile $zip -Headers @{ 'User-Agent' = 'OPTCG-Trainer-Installer' } -TimeoutSec 120 -UseBasicParsing

    $digest = [string]$bestAsset.digest
    if ($digest -and $digest.StartsWith('sha256:', [StringComparison]::OrdinalIgnoreCase)) {
        $expected = $digest.Substring(7).ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { throw "Downloaded file failed integrity check (expected $expected, got $actual)." }
    }

    Write-Host '  Extracting...'
    Expand-Archive -LiteralPath $zip -DestinationPath $pkg -Force

    $installer = Join-Path $pkg 'files\install.ps1'
    if (-not (Test-Path -LiteralPath $installer)) { throw 'The downloaded package does not contain files\install.ps1.' }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
    exit $LASTEXITCODE
}
catch {
    Write-Host ''
    Write-Host "  ERROR: $($_.Exception.Message)"
    exit 1
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -Recurse -Force -LiteralPath $tmp -ErrorAction SilentlyContinue }
}
