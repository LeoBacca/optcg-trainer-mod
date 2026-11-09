# OPTCG Trainer for OPTCGSim

A mod for [OPTCGSim](https://optcgsim.com) that brings [OPTCG Trainer](https://optcgtrainer.com) inside the sim:
train for your next event with the Event Coach, write notes while you play, play in Focus mode, save a picture of the
game to share, and find every game already on your OPTCG Trainer account, with your notes at the right moment.

> **Beta.** Windows only. This is an unofficial fan project, not made or endorsed by Batsu (OPTCGSim) or Bandai.

![The OPTCG TRAINER button in the OPTCGSim main menu](docs/images/menu.jpg)

## Install (2 minutes)

1. Download **`OPTCG-Trainer-x.y.z.zip`** from the [Releases page](https://github.com/LeoBacca/optcg-trainer-mod/releases) (the newest one is at the top).
2. Right-click the zip → **Extract All…** → **Extract**.
3. Close OPTCGSim, then double-click **`Install OPTCG Trainer`** in the extracted folder.
   - If Windows asks *"Do you want to run this file?"* press **Run**.
   - It finds your OPTCGSim by itself. If you have more than one, pick the one you play with.
4. Open **OPTCGSim + Trainer** from your desktop or the Start menu. **Use it from now on**: the usual OPTCGSim still starts without the mod.
   (To pin it to the taskbar: right-click it → *Pin to taskbar*.)
5. In the main menu press the blue **OPTCG TRAINER** button. Your browser opens OPTCG Trainer: check the code is the same
   you see in the sim and press **Link**. You do this once.

That's it. Play as usual.

## What you get

**Event Coach.** Getting ready for a regional? Set the event date, the day you want to be ready by and how many games
you want to play: the coach works out how many games a day, and keeps track of them inside OPTCGSim.
Open it from *Settings › EVENT COACH* (once your event is set, it's in the main menu too).
- *Today*: today's games, your streak of days (rest days you plan don't break it) and a 20-level pass up to EVENT READY.
- *Stats*: a calendar of the games you played, win rate going first and second, matchups by opponent leader,
  games per week against your plan.
- Today's goal is right there on the Private screen, and at the end of each game a line tells you if it counted.

Games count if you play them with the mod, in the event format, and they reach turn 3 (ladder, queue and private;
Solo v Self if you want). Everything stays on your PC, in `%LOCALAPPDATA%\OPTCGTrainer\coach.json`.

![Event Coach statistics](docs/images/coach.jpg)

![Today's goal on the Private screen](docs/images/coach-private.jpg)

**Notes while you play.** *Add note* opens a box over the log: write what you're thinking
(*"maybe I shouldn't have played X here"*). The note is pinned to that exact moment of the game, even if the game goes on
while you type, and you'll find it there in the replay.

![Writing a note during a game](docs/images/note.jpg)

**Focus mode.** Only the board, the choices, Back to Main and Concede, in full screen (like F11 in a browser).
No log, no chat. Press **Esc** or *Exit focus mode* to go back. It stays on for your next games until you turn it off.

![Focus mode](docs/images/focus.jpg)

**A picture to share.** The camera icon in the bottom-right corner saves a picture made for Discord or your coach:
the board, plus what a screenshot doesn't show — both trashes, the cards you put at the bottom of your deck, and the cards
you know are in your opponent's hand. Copies of the same card are stacked with a number. It goes to *Pictures › OPTCG Trainer*.

![The share picture](docs/images/share.jpg)

It only shows what you really know: the deck bottom is the cards you put there one by one (a shuffle clears it);
a card in your opponent's hand is known only if it came from the board or the trash, or it was revealed.

**Your games on your account.** When a game ends it goes to your OPTCG Trainer account by itself, with your notes.
*Open this game in OPTCG Trainer* opens the replay. Games you leave halfway (Back to Main) are uploaded too.
Each game also knows its type: ladder (OPBounty), queue, private lobby or Solo, and the queue (Standard, OP18…),
so on OPTCG Trainer you can filter your games by type.
A copy of every game log is also kept in `%LOCALAPPDATA%\OPTCGTrainer\logs`: OPTCGSim updates delete its own `CombatLogs` folder.

**A cleaner Multiplayer screen.** The sponsor banner, *TCG Match Making* (Discord), *Match History* and the
*Open OPBounty* button at the top right are hidden: *Play Ranked / Launch OPBounty* and everything else stay. To bring them back, set `HideExtras = false` under `[Multiplayer]`
in `%LOCALAPPDATA%\OPTCGTrainer\BepInEx\config\com.optcgtrainer.mod.cfg`.

**OPBounty room codes pasted for you.** When the OPBounty program copies a room code for you (*PLEASE PASTE LOBBY CODE*),
the mod puts it in the code field of the Private screen (the one *Multiplayer* opens). You still press the button to enter.
The mod only reads that code: it never talks to OPBounty.

![End of the game](docs/images/end.jpg)

## What it changes on your PC

- **In your OPTCGSim folder: one file, `winhttp.dll`** (Unity Doorstop, the loader used by BepInEx). Nothing of OPTCGSim is modified.
- Everything else is in `%LOCALAPPDATA%\OPTCGTrainer`: BepInEx, the mod, your notes, the log copies.
- A shortcut on your desktop and in the Start menu: *OPTCGSim + Trainer*.

## What it doesn't do

- It doesn't change the rules, the cards, the network or anything that decides a game. It only adds buttons and panels on your screen.
- It never writes into OPTCGSim's own game log.
- It talks to `optcgtrainer.com` only after you link your account: it uploads your game logs (with the game type) and your notes, nothing else.
  The link can be removed any time from OPTCG Trainer → Settings → *OPTCGSim linked*.
- When OPTCGSim starts it asks GitHub (this page) if there's a newer version of the mod. It sends nothing about you or your games.
- The code is all here: read it, build it yourself if you prefer.

## Updating the mod (from 0.4.0)

When a new version is out, the small line above the **OPTCG TRAINER** button says **Update to x.y.z: click here**.
Click it: the mod downloads only its own file (`OPTCGTrainer.dll`) from the Releases page, checks it's exactly the one published
there, and puts it in place. Then restart OPTCGSim. Nothing is downloaded until you click, and your notes and link stay as they are.

Don't want it to ask? Set `Check = false` under `[Updates]` in `%LOCALAPPDATA%\OPTCGTrainer\BepInEx\config\com.optcgtrainer.mod.cfg`.
On 0.3.x there's no such line: download the new zip once and run **Install OPTCG Trainer** again.

## When OPTCGSim updates

The mod survives updates: OPTCGSim's patcher keeps `winhttp.dll`, and the rest is outside its folder.
If an update changes something the mod uses, that part turns itself off and a small line at the top of the screen says so;
OPTCGSim keeps working as usual. When a fixed version is out, the update line above the OPTCG TRAINER button offers it.

## Uninstall

Double-click **`Uninstall OPTCG Trainer`** (in the folder you extracted). It removes `winhttp.dll`, the shortcuts and
the mod. Your notes and log copies stay in `%LOCALAPPDATA%\OPTCGTrainer`: delete that folder to remove them too.

## Problems?

Open an issue here, and attach `%LOCALAPPDATA%\OPTCGTrainer\BepInEx\LogOutput.log`.
Some antivirus programs dislike `winhttp.dll` because it loads mods into a game: it's the standard
[Unity Doorstop](https://github.com/NeighTools/UnityDoorstop) file shipped with [BepInEx](https://github.com/BepInEx/BepInEx).

## For developers

The mod is a [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin ([HarmonyX](https://github.com/BepInEx/HarmonyX) patches),
built against your own copy of OPTCGSim (its DLLs are not in this repo):

```sh
dotnet build plugin -c Release -p:SimDir="C:\path\to\OPTCGSim" -p:BepInExCore="C:\path\to\BepInEx\core" -p:DevTools=false
```

- `plugin/` — the mod. Comments are in Italian.
- `installer/` — the install / uninstall scripts. `tools/pacchetto.ps1` builds the release zip.
- How the mod talks to optcgtrainer.com (device linking, uploads) is in `docs/site-api.md`.

License: MIT. Bundled third-party parts and their licenses are listed in the release zip (`files/licenses`).
