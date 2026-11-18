// La mod torna da sola dopo un aggiornamento di OPTCGSim.
// Il patcher di Batsu (StreamingAssets\PatcherData\DoPatch.ps1, lanciato da UpdateCheckScript) costruisce la versione nuova in
// ..\OPSIMTEMPINSTALL (hpatchz), chiude il sim, cancella la cartella del sim tranne .exe/.dll/.deck, ci copia dentro la versione
// nuova e la riapre con ".\OPTCGSim.exe", senza argomenti: senza --doorstop-enabled il sim nuovo partiva senza mod
// (successo il 2026-11-18, 1.45a -> 1.45b). Qui la mod ci passa sopra senza toccare niente di Batsu:
//   1. mentre gira il controllo degli aggiornamenti, un thread guarda se compare ..\OPSIMTEMPINSTALL. Se compare ci scrive un
//      doorstop_config.ini (acceso, con il percorso di BepInEx) e lo rimette se sparisce, finché il patcher chiude il sim.
//      È la copia finale del patcher a portarlo nella cartella del sim: il sim nuovo lo legge e parte con la mod.
//      La cartella non la crea mai la mod (hpatchz senza -f si ferma se esiste già), e in una cartella vecchia rimasta da un
//      aggiornamento non riuscito non scrive niente (il patcher la cancella, e un file nostro a metà cancellazione lo fermerebbe).
//   2. Doorstop mette due variabili nel processo, e i processi figli le ereditano (sim -> powershell -> sim nuovo):
//      DOORSTOP_DISABLE=TRUE (src/windows/entrypoint.c, appena messi i suoi agganci: il Doorstop del figlio si spegne) e
//      DOORSTOP_INITIALIZED=TRUE (src/bootstrap.c: il figlio salta il caricamento). UnityDoorstop 4.5.0, provato nella copia di
//      prova. Si tolgono dal processo prima che parta il patcher: a questo processo non servono più (Doorstop le guarda solo
//      all'avvio, e dal secondo caricamento si protegge con un flag suo in memoria). In più l'ini ha ignore_disable_switch.
//   3. al riavvio la mod trova il suo doorstop_config.ini e lo cancella: nella cartella del sim resta solo winhttp.dll, e il doppio
//      clic su OPTCGSim.exe apre il sim senza mod come sempre.
// Tutto in try/catch e niente processi in più: se qualcosa va storto, l'aggiornamento finisce come prima, solo senza mod.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OPTCGTrainer
{
    static class SimUpdate
    {
        const string IniName = "doorstop_config.ini";
        const string TempName = "OPSIMTEMPINSTALL";
        // la riga che dice che il file è della mod: solo quello si cancella
        const string Mark = "# OPTCG Trainer:";
        // le variabili che Doorstop mette nel processo e che spegnerebbero il Doorstop del sim riaperto dal patcher
        static readonly string[] DoorstopVars = { "DOORSTOP_DISABLE", "DOORSTOP_INITIALIZED" };
        static readonly TimeSpan MaxWatch = TimeSpan.FromMinutes(30);

        static Thread watcher;
        static volatile bool stop;
        static string watchScene;

        // All'avvio: un doorstop_config.ini della mod vuol dire che il sim è appena ripartito dal patcher (con la mod). Ha fatto
        // il suo lavoro e si toglie. Doorstop lo legge dalla cartella in cui parte il sim, che per il patcher è quella del sim.
        internal static void CleanUp()
        {
            foreach (var dir in new[] { Directory.GetCurrentDirectory(), Paths.GameRootPath }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var file = Path.Combine(dir, IniName);
                    if (!File.Exists(file) || !File.ReadAllText(file).Contains(Mark)) continue;
                    File.Delete(file);
                    Plugin.Log.LogInfo($"mod tornata dopo l'aggiornamento del sim (ora {Application.version}): tolto il {IniName} che la mod aveva messo");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"{IniName} della mod non tolto da {dir}: {e.Message}"); }
            }
        }

        // Il controllo degli aggiornamenti del sim sta partendo (o è già partito): da qui al cambio di scena, se compare la
        // cartella del patcher, ci va il doorstop_config.ini. Si può chiamare più volte: un thread solo alla volta.
        internal static void Begin(string scene)
        {
            try
            {
                if (watcher != null && watcher.IsAlive) return;
                foreach (var name in DoorstopVars)
                {
                    var was = Environment.GetEnvironmentVariable(name);
                    if (was == null) continue;
                    Environment.SetEnvironmentVariable(name, null);
                    Plugin.Log.LogInfo($"tolta {name}={was} dalle variabili del processo (se no il sim riaperto dal patcher parte senza mod)");
                }
                var preloader = Path.Combine(Paths.BepInExRootPath, "core", "BepInEx.Preloader.dll");
                if (!File.Exists(preloader)) { Plugin.Log.LogWarning("aggiornamento del sim: BepInEx.Preloader.dll non trovato in " + preloader); return; }
                var parent = Directory.GetParent(Directory.GetCurrentDirectory());
                if (parent == null) return;   // il sim gira dalla radice di un disco: il patcher non avrebbe dove lavorare
                var temp = Path.Combine(parent.FullName, TempName);
                var text = string.Join("\r\n",
                    Mark + " written while OPTCGSim was updating itself, so the new version starts with the mod.",
                    "# OPTCG Trainer deletes this file as soon as it starts: opening OPTCGSim.exe normally still starts the game without the mod.",
                    "[General]",
                    "enabled = true",
                    "target_assembly = " + preloader,
                    "ignore_disable_switch = true",
                    "");
                watchScene = scene;
                stop = false;
                watcher = new Thread(() => Watch(temp, text)) { IsBackground = true, Name = "OPTCG Trainer: aggiornamento del sim" };
                watcher.Start();
            }
            catch (Exception e) { Plugin.Log.LogWarning("aggiornamento del sim: non guardo (" + e.Message + ")"); }
        }

        // L'unico posto (con UpdateCheckStartPatch) che nomina UpdateCheckScript: a parte e mai incorporato in chi lo chiama, così
        // se Batsu toglie o rinomina la classe si ferma solo questo (un'eccezione nel try di Plugin.Awake), non tutto Awake
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void BeginIfChecking()
        {
            var check = UnityEngine.Object.FindAnyObjectByType<UpdateCheckScript>();
            if (check != null) Begin(check.gameObject.scene.name);
        }

        // un'altra scena = il controllo degli aggiornamenti è finito (il sim fa LoadScene("main") anche quando il patcher non riesce)
        internal static void SceneLoaded(Scene scene)
        {
            if (watcher != null && watcher.IsAlive && scene.name != watchScene) stop = true;
        }

        static void Watch(string temp, string text)
        {
            var ini = Path.Combine(temp, IniName);
            var until = DateTime.UtcNow + MaxWatch;
            // una cartella che c'è già è di un aggiornamento finito male: il patcher la cancella e poi hpatchz la rifà. Si aspetta di
            // vederla sparire (o hpatchz al lavoro: allora la cancellazione è finita) prima di scriverci.
            bool old = Directory.Exists(temp), wrote = false;
            int round = 0;
            try
            {
                if (old) Plugin.Log.LogInfo("aggiornamento del sim: c'è una " + TempName + " vecchia, aspetto che il patcher la tolga");
                while (!stop && DateTime.UtcNow < until)
                {
                    bool exists = Directory.Exists(temp);
                    if (!exists) old = false;
                    else if (old && round % 100 == 0 && Patching()) old = false;   // una volta al secondo
                    if (exists && !old)
                    {
                        try
                        {
                            if (!File.Exists(ini))
                            {
                                // WriteAllText non crea la cartella: se nel frattempo è sparita, eccezione e si riprova al giro dopo
                                File.WriteAllText(ini, text, Encoding.Unicode);
                                Plugin.Log.LogInfo((wrote ? "rimesso " : "aggiornamento del sim in corso: messo ") + ini);
                                wrote = true;
                            }
                        }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    }
                    round++;
                    // veloce mentre si aspetta che la cartella vecchia sparisca (tra la cancellazione e hpatchz passa poco)
                    Thread.Sleep(old ? 10 : 100);
                }
                Plugin.Log.LogInfo(wrote ? "aggiornamento del sim: il patcher non ha chiuso il sim (non riuscito?)"
                                         : "nessun aggiornamento del sim in corso: smetto di guardare");
            }
            catch (Exception e) { Plugin.Log.LogWarning("aggiornamento del sim: " + e.Message); }
        }

        // hpatchz (il primo programma del patcher dopo la cancellazione della cartella vecchia) sta girando?
        static bool Patching()
        {
            try
            {
                var found = Process.GetProcessesByName("hpatchz");
                foreach (var p in found) p.Dispose();
                return found.Length > 0;
            }
            catch { return false; }
        }

#if DEVTOOLS
        // per le prove: rimette le variabili come le lascia Doorstop (cioè come una mod senza questa correzione)
        internal static void PutBackDoorstopVars() { foreach (var name in DoorstopVars) Environment.SetEnvironmentVariable(name, "TRUE"); }
#endif
    }

    // il controllo degli aggiornamenti del sim parte qui (prima scena): da ora si guarda se arriva il patcher
    [HarmonyPatch(typeof(UpdateCheckScript), "Start")]
    static class UpdateCheckStartPatch
    {
        static void Prefix(UpdateCheckScript __instance)
        {
            try { SimUpdate.Begin(__instance.gameObject.scene.name); }
            catch (Exception e) { Plugin.Log.LogWarning("aggiornamento del sim: " + e.Message); }
        }
    }
}
