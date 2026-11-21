# OPTCG Trainer - installer (Windows). Double-click "Install OPTCG Trainer.cmd" to run it.
#
# What it does, and nothing else:
#   1. finds your OPTCGSim folder (or asks you to pick it)
#   2. adds two small files to it: winhttp.dll (BepInEx's loader, Unity Doorstop) and doorstop_config.ini, so OPTCGSim
#      starts with the mod however you open it. Nothing of OPTCGSim is changed.
#   3. puts BepInEx and the mod in %LOCALAPPDATA%\OPTCGTrainer (outside the game folder, so OPTCGSim updates don't touch it)
#   4. creates the "OPTCGSim + Trainer" shortcut on your desktop: that one starts OPTCGSim with the mod.
# Undo everything with "Uninstall OPTCG Trainer.cmd".
param([string]$SimDir = "", [switch]$Quiet, [string]$ShortcutDir = "", [string]$HomeDir = "")   # -HomeDir: only for tests (keeps a real install untouched)
$ErrorActionPreference = "Stop"
# everything the installer does goes to this file: send it to Leo if something doesn't work
try { Start-Transcript -Path (Join-Path $env:TEMP "optcgtrainer-install.log") -Force | Out-Null } catch { }
Add-Type -AssemblyName System.Windows.Forms
$here = $PSScriptRoot
$title = "OPTCG Trainer"

function Say([string]$text, [string]$icon = "Information") {
    if ($Quiet) { Write-Host $text; return }
    [System.Windows.Forms.MessageBox]::Show($text, $title, "OK", $icon) | Out-Null
}

# ---- 1. find OPTCGSim ----
function Find-Sims {
    $found = New-Object System.Collections.Generic.List[string]
    # where to look and how deep: your own folders in depth, the rest only near the top (a whole drive would take ages)
    $roots = @(
        @([Environment]::GetFolderPath("Desktop"), 6), @([Environment]::GetFolderPath("MyDocuments"), 6), @((Join-Path $env:USERPROFILE "Downloads"), 6),
        @($env:USERPROFILE, 3), @($env:ProgramFiles, 3), @(${env:ProgramFiles(x86)}, 3), @("C:\Games", 3))
    foreach ($d in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -ne "C:\" -and $_.Used -ne $null })) { $roots += ,@($d.Root, 3) }
    $skip = @('AppData', 'node_modules', '.git', 'Windows', '$Recycle.Bin', '_sim-prova')
    $seen = @{}
    foreach ($r in $roots) {
        $root = $r[0]; $max = $r[1]
        if (-not $root -or -not (Test-Path $root)) { continue }
        $queue = New-Object System.Collections.Generic.Queue[object]
        $queue.Enqueue(@($root, 0))
        while ($queue.Count -gt 0) {
            $item = $queue.Dequeue(); $dir = $item[0]; $depth = $item[1]
            if ($seen.ContainsKey($dir)) { continue }; $seen[$dir] = $true
            if (Test-Path -LiteralPath (Join-Path $dir "OPTCGSim.exe")) { $found.Add($dir); continue }
            if ($depth -ge $max) { continue }
            try { $subs = [System.IO.Directory]::GetDirectories($dir) } catch { continue }
            foreach ($sub in $subs) { if ($skip -notcontains (Split-Path $sub -Leaf)) { $queue.Enqueue(@($sub, $depth + 1)) } }
        }
    }
    return $found | Select-Object -Unique
}

function Pick-Folder {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select the OPTCGSim folder (the one with OPTCGSim.exe inside)"
    if ($dialog.ShowDialog() -ne "OK") { return $null }
    return $dialog.SelectedPath
}

function Pick-FromList($sims) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$title - which OPTCGSim?"; $form.Width = 720; $form.Height = 300; $form.StartPosition = "CenterScreen"
    $label = New-Object System.Windows.Forms.Label
    $label.Text = "I found more than one OPTCGSim. Which one do you play with?"; $label.AutoSize = $true; $label.Top = 12; $label.Left = 12
    $list = New-Object System.Windows.Forms.ListBox
    $list.Top = 40; $list.Left = 12; $list.Width = 680; $list.Height = 160
    foreach ($s in $sims) { [void]$list.Items.Add($s) }
    $list.SelectedIndex = 0
    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Install here"; $ok.Top = 215; $ok.Left = 12; $ok.Width = 140; $ok.DialogResult = "OK"
    $other = New-Object System.Windows.Forms.Button
    $other.Text = "Another folder..."; $other.Top = 215; $other.Left = 165; $other.Width = 140; $other.DialogResult = "Retry"
    $form.Controls.AddRange(@($label, $list, $ok, $other)); $form.AcceptButton = $ok
    $r = $form.ShowDialog()
    if ($r -eq "OK") { return $list.SelectedItem }
    if ($r -eq "Retry") { return Pick-Folder }
    return $null
}

# the quick way: OPTCGSim writes in its Unity log which folder it started from (the copy you actually play with)
function Recent-Sims {
    $logs = Join-Path $env:USERPROFILE "AppData\LocalLow\Batsu\OPTCGSim"
    $out = @()
    foreach ($name in "Player.log", "Player-prev.log") {
        $f = Join-Path $logs $name
        if (-not (Test-Path $f)) { continue }
        $line = Get-Content $f -TotalCount 3 | Where-Object { $_ -like "Mono path*" } | Select-Object -First 1
        if ($line -match "'(.+)/OPTCGSim_Data/Managed'") {
            $dir = $Matches[1].Replace('/', '\')
            if ($dir -notlike "*\_sim-prova*" -and (Test-Path -LiteralPath (Join-Path $dir "OPTCGSim.exe"))) { $out += $dir }
        }
    }
    return $out | Select-Object -Unique
}

try {
    $home_ = if ($HomeDir) { $HomeDir } else { Join-Path $env:LOCALAPPDATA "OPTCGTrainer" }
    $isUpdate = $false
    if (-not $SimDir) {
        $prevInfo = Join-Path $home_ "install.json"
        if (Test-Path -LiteralPath $prevInfo) {
            try {
                $prevDir = (Get-Content -LiteralPath $prevInfo -Raw | ConvertFrom-Json).simDir
                if ($prevDir -and (Test-Path -LiteralPath (Join-Path $prevDir "OPTCGSim.exe"))) {
                    $SimDir = $prevDir
                    $isUpdate = $true
                }
            } catch { }
        }
    }
    if (-not $SimDir) {
        $sims = @(Recent-Sims)
        if ($sims.Count -eq 0) {
            if (-not $Quiet) { Write-Host "Looking for OPTCGSim on this PC... (it can take a minute)" }
            $sims = @(Find-Sims)
        }
        if ($sims.Count -eq 1) { $SimDir = $sims[0] }
        elseif ($sims.Count -gt 1) { $SimDir = Pick-FromList $sims }
        else { Say "I couldn't find OPTCGSim. Please select its folder (the one with OPTCGSim.exe inside)."; $SimDir = Pick-Folder }
    }
    if (-not $SimDir) { exit 0 }   # you cancelled
    if (-not (Test-Path -LiteralPath (Join-Path $SimDir "OPTCGSim.exe"))) { Say "There's no OPTCGSim.exe in:`n$SimDir`n`nNothing was installed." "Warning"; exit 0 }

    # ---- 2. OPTCGSim must be closed (its files are in use while it runs) ----
    while (Get-Process OPTCGSim -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($SimDir) }) {
        if ($Quiet) { Write-Host "OPTCGSim is open: close it first."; exit 1 }
        $r = [System.Windows.Forms.MessageBox]::Show("Please close OPTCGSim, then press OK.", $title, "OKCancel", "Warning")
        if ($r -ne "OK") { exit 0 }
    }

    # ---- 3. the files ----
    $bep = Join-Path $home_ "BepInEx"
    New-Item -ItemType Directory -Force (Join-Path $bep "core"), (Join-Path $bep "plugins") | Out-Null
    Copy-Item -Force (Join-Path $here "BepInEx\core\*") (Join-Path $bep "core")
    Copy-Item -Force (Join-Path $here "OPTCGTrainer.dll") (Join-Path $bep "plugins")
    Copy-Item -Force (Join-Path $here "optcgtrainer.ico") $home_

    $ours = Join-Path $here "winhttp.dll"
    $theirs = Join-Path $SimDir "winhttp.dll"
    $oursHash = (Get-FileHash $ours).Hash
    if ((Test-Path $theirs) -and ((Get-FileHash $theirs).Hash -ne $oursHash)) {
        Copy-Item -Force $theirs "$theirs.before-optcgtrainer"   # another mod loader was there: kept aside, Uninstall puts it back
    }
    Copy-Item -Force $ours $theirs

    # doorstop_config.ini lets users keep opening their usual OPTCGSim.exe (and any existing shortcut) with the mod active
    $preloader = Join-Path $bep "core\BepInEx.Preloader.dll"
    $ini = Join-Path $SimDir "doorstop_config.ini"
    if ((Test-Path -LiteralPath $ini) -and ((Get-Content -LiteralPath $ini -Raw) -notmatch '# OPTCG Trainer:')) {
        Copy-Item -Force -LiteralPath $ini "$ini.before-optcgtrainer"
    }
    @(
        "# OPTCG Trainer: loads the mod whenever OPTCGSim starts (uninstall removes this file)."
        "[General]"
        "enabled = true"
        "target_assembly = $preloader"
        "ignore_disable_switch = true"
    ) | Set-Content -Encoding Unicode -LiteralPath $ini

    @{ simDir = $SimDir; installedAt = (Get-Date).ToString("o"); winhttpHash = $oursHash } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $home_ "install.json")
    if (Test-Path (Join-Path $here "uninstall.ps1")) {
        Copy-Item -Force (Join-Path $here "uninstall.ps1") (Join-Path $home_ "uninstall.ps1")
        "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0uninstall.ps1`"`r`nif errorlevel 1 pause`r`n" | Set-Content -Encoding ASCII (Join-Path $home_ "Uninstall OPTCG Trainer.cmd")
    }

    # ---- 4. the shortcut (kept updated for users who already launch 'OPTCGSim + Trainer') ----
    $desktop = if ($ShortcutDir) { $ShortcutDir } else { [Environment]::GetFolderPath("Desktop") }
    $lnk = Join-Path $desktop "OPTCGSim + Trainer.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $s = $shell.CreateShortcut($lnk)
    $s.TargetPath = Join-Path $SimDir "OPTCGSim.exe"
    $s.WorkingDirectory = $SimDir
    $s.Arguments = "--doorstop-enabled true --doorstop-target-assembly `"$preloader`""
    $s.IconLocation = (Join-Path $home_ "optcgtrainer.ico") + ",0"
    $s.Description = "OPTCGSim with OPTCG Trainer"
    $s.Save()
    # the same shortcut in the Start menu (All apps, and you can search it); pinning it is up to you: right-click > Pin
    if (-not $ShortcutDir) { Copy-Item -Force $lnk (Join-Path ([Environment]::GetFolderPath("Programs")) "OPTCGSim + Trainer.lnk") }

    if ($isUpdate) {
        Say "OPTCG Trainer is updated!`n`nKeep opening OPTCGSim (or 'OPTCGSim + Trainer') just like before."
    } else {
        Say "OPTCG Trainer is installed!`n`nYou can open OPTCGSim just like before (or 'OPTCGSim + Trainer' from your desktop).`n`nThe first time, press the blue OPTCG TRAINER button in the main menu to link your OPTCG Trainer account."
    }
}
catch {
    $msg = $_.Exception.Message
    Write-Host "ERROR: $msg"
    Write-Host $_.ScriptStackTrace
    if (-not $Quiet) { [System.Windows.Forms.MessageBox]::Show("Something went wrong:`n`n$msg`n`nSend a screenshot of this message to Leo, with the file:`n$env:TEMP\optcgtrainer-install.log", $title, "OK", "Error") | Out-Null }
    exit 1
}
finally { try { Stop-Transcript | Out-Null } catch { } }
