# Prepara lo zip da dare a chi installa: dist\OPTCG-Trainer-<versione>.zip
#   Install OPTCG Trainer.cmd · Uninstall OPTCG Trainer.cmd · README.txt
#   files\ install.ps1, uninstall.ps1, winhttp.dll (Doorstop), BepInEx\core, OPTCGTrainer.dll, optcgtrainer.ico, licenze
# Il plugin si compila senza il telecomando delle prove (-p:DevTools=false) e con la versione di Plugin.cs anche dentro il dll.
# Uso: powershell -File tools\pacchetto.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$version = [regex]::Match((Get-Content (Join-Path $root "plugin\Plugin.cs") -Raw), 'Version = "([^"]+)"').Groups[1].Value
$pack = Join-Path $root "_tools\bepinex-5.4.23.5\pack"
$build = Join-Path $root "_tools\build-pacchetto"
$out = Join-Path $root "dist\OPTCG-Trainer-$version"
$zip = "$out.zip"

# OPBounty (il programma del ranked) legge il Player.log del sim, dove finiscono anche le righe della mod: nessuna di
# queste frasi deve mai comparire nel codice della mod, se no una riga nostra potrebbe valere come vittoria o sconfitta
# (elenco da docs/ricerca-opbounty.md)
$opbounty = 'match ready on Room ID', 'load my deck', 'Host with', 'Join with', 'Return to Main', 'player requests win',
  'player requests loss', 'player conceded', 'opponent conceded', 'OPB_RESULT|', 'match cancelled', 'opponent connection lost',
  'opponent disconnected', 'Destroy Lobbies', 'Set up Unity Multiplayer', 'Memory Statistics'
$hits = Select-String -Path (Join-Path $root 'plugin\*.cs') -SimpleMatch -Pattern $opbounty
if ($hits) { $hits | ForEach-Object { "  $($_.Filename):$($_.LineNumber): $($_.Line.Trim())" }; throw 'nel codice della mod ci sono frasi che OPBounty legge nel Player.log' }

& "$env:USERPROFILE\.dotnet\dotnet.exe" build (Join-Path $root "plugin") -c Release -p:DevTools=false -p:Version=$version -o $build -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "compilazione non riuscita" }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
$files = Join-Path $out "files"
New-Item -ItemType Directory -Force (Join-Path $files "BepInEx\core"), (Join-Path $files "licenses") | Out-Null
Copy-Item (Join-Path $root "installer\*.cmd"), (Join-Path $root "installer\README.txt") $out
Copy-Item (Join-Path $root "installer\files\*.ps1") $files
Copy-Item (Join-Path $pack "winhttp.dll") $files
Copy-Item (Join-Path $pack "BepInEx\core\*.dll") (Join-Path $files "BepInEx\core")
Copy-Item (Join-Path $build "OPTCGTrainer.dll") $files
Copy-Item (Join-Path $root "plugin\assets\icona.ico") (Join-Path $files "optcgtrainer.ico")
Copy-Item (Join-Path $root "_tools\LICENSE-*.txt") (Join-Path $files "licenses")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $files "licenses\LICENSE-OPTCGTrainer.txt")
@"
OPTCG Trainer $version bundles, unmodified:
- BepInEx 5.4.23.5 (MIT) - https://github.com/BepInEx/BepInEx
- Unity Doorstop 4.5.0, the winhttp.dll file (LGPL-2.1) - https://github.com/NeighTools/UnityDoorstop
- HarmonyX (MIT) - https://github.com/BepInEx/HarmonyX
- MonoMod (MIT) - https://github.com/MonoMod/MonoMod
- Mono.Cecil (MIT) - https://github.com/jbevain/cecil
Their license texts are in this folder. OPTCG Trainer itself is MIT: LICENSE-OPTCGTrainer.txt.
OPTCGSim is made by Batsu; OPTCG Trainer is a fan project and is not affiliated with it.
"@ | Set-Content -Encoding UTF8 (Join-Path $files "licenses\THIRD-PARTY.txt")

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
"pronto: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
# per l'aggiornamento con un clic (plugin/Updater.cs) la release su GitHub deve avere anche questo file, con questo nome
$dll = Join-Path $files "OPTCGTrainer.dll"
$inside = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
if ("$inside" -ne "$version.0") { throw "nel dll c'e' la versione $inside, non la $version.0: Updater.cs lo rifiuterebbe" }
"e per la release (con questo nome): $dll (versione dentro: $inside)"
"  gh release create v$version `"$zip`" `"$dll`" --prerelease --title `"OPTCG Trainer $version (beta)`" --notes-file <note.md>"
