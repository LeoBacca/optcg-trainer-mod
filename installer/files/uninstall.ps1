# OPTCG Trainer - uninstaller. Double-click "Uninstall OPTCG Trainer.cmd" to run it.
# Removes winhttp.dll from the OPTCGSim folder (only if it's ours), the desktop shortcut and BepInEx + the mod.
# Your notes and the copies of your game logs stay in %LOCALAPPDATA%\OPTCGTrainer (delete that folder to remove them too).
param([switch]$Quiet, [string]$ShortcutDir = "")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
$here = $PSScriptRoot
$title = "OPTCG Trainer"
function Say([string]$text) { if ($Quiet) { Write-Host $text } else { [System.Windows.Forms.MessageBox]::Show($text, $title) | Out-Null } }

$home_ = Join-Path $env:LOCALAPPDATA "OPTCGTrainer"
$info = Join-Path $home_ "install.json"
$simDir = if (Test-Path $info) { (Get-Content $info -Raw | ConvertFrom-Json).simDir } else { $null }

if ($simDir) {
    if (Get-Process OPTCGSim -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($simDir) }) { Say "Please close OPTCGSim first, then run Uninstall again."; exit 1 }
    $dll = Join-Path $simDir "winhttp.dll"
    if ((Test-Path $dll) -and ((Get-FileHash $dll).Hash -eq (Get-FileHash (Join-Path $here "winhttp.dll")).Hash)) { Remove-Item -Force $dll }
    if (Test-Path "$dll.before-optcgtrainer") { Move-Item -Force "$dll.before-optcgtrainer" $dll }
}
$desktop = if ($ShortcutDir) { $ShortcutDir } else { [Environment]::GetFolderPath("Desktop") }
Remove-Item -Force (Join-Path $desktop "OPTCGSim + Trainer.lnk") -ErrorAction SilentlyContinue
if (-not $ShortcutDir) { Remove-Item -Force (Join-Path ([Environment]::GetFolderPath("Programs")) "OPTCGSim + Trainer.lnk") -ErrorAction SilentlyContinue }
Remove-Item -Recurse -Force (Join-Path $home_ "BepInEx") -ErrorAction SilentlyContinue
Remove-Item -Force $info, (Join-Path $home_ "optcgtrainer.ico") -ErrorAction SilentlyContinue

Say "OPTCG Trainer was removed. OPTCGSim is exactly as before.`n`nYour notes and log copies are still in:`n$home_"
