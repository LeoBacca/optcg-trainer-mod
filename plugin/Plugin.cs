// OPTCG Trainer — mod per OPTCGSim. Porta OPTCG Trainer dentro il sim:
//   menu principale  il pulsante OPTCG TRAINER (MenuButton.cs)
//   in partita       Add note, Focus mode, a fine partita "apri su OPTCG Trainer" (GameUi.cs, Match.cs)
//   sito             collegamento dell'account e caricamento di log e note (Account.cs, docs/api-sito.md)
//   aggiornamenti    "Update to x.y.z" sopra il pulsante quando su GitHub c'è una release nuova (Updater.cs)
//   multiplayer      via sponsor, Discord, Match History e «Open OPBounty» in alto a destra (CleanMultiplayer.cs);
//                    il codice della stanza di OPBounty messo da solo nel campo di Private (PasteCode.cs)
//   Event Coach      un piano di partite per un torneo: Setup da Settings, Today e Stats, il riquadro in Private, la riga a fine
//                    partita (CoachUi.cs e le altre Coach*.cs; i calcoli in Coach.cs, i file in CoachStore.cs e CoachFiles.cs)
// Non cambia le regole né la rete del sim: aggiunge interfaccia, legge il log, scrive solo file suoi (Store.cs).
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OPTCGTrainer
{
    [BepInPlugin(Guid, "OPTCG Trainer", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.optcgtrainer.mod";
        public const string Version = "0.4.1";
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static Sprite LogoSprite;
        internal static ConfigEntry<bool> FocusMode, FocusFullscreen, CheckUpdates, HideMultiplayerExtras, PasteOPBountyCode;
        internal static ConfigEntry<string> WindowBeforeFocus;
        static ConfigEntry<string> site;
        internal static string SiteUrl => site.Value;
        static int missingHooks;
        GUIStyle warning, updateButton;
        // i pezzi della mod che girano a ogni fotogramma, ognuno per conto suo: se uno va in errore (versione nuova del sim) gli altri continuano
        static readonly (string name, Action tick)[] Ticks =
        {
            ("menu", MenuButton.FollowSource), ("partita", GameUi.Tick), ("telecomando", DevCommands.Tick), ("multiplayer", CleanMultiplayer.Tick),
            ("incolla", PasteCode.Tick), ("coach", CoachStore.Tick), ("schermate del coach", CoachUi.Tick),
        };
        readonly string[] lastError = new string[Ticks.Length];

        // Tutto quello che la mod usa del sim, in un posto solo: a ogni versione nuova si guarda qui se qualcosa è sparito.
        static readonly (string type, string member)[] Hooks =
        {
            ("GameplayLogicScript", "SaveMyLogLines"),
            ("GameplayLogicScript", "ReplaySync_Reset"),
            ("GameplayLogicScript", "OnDestroy"),
            ("GameplayLogicScript", "GameStateStepTurnDirection"),
            ("GameplayLogicScript", "OnApplicationQuit"),
            ("GameplayLogicScript", "HandleKeyboardAction"),
            ("GameplayLogicScript", "HandleMouseHover"),
            ("GameplayLogicScript", "HandleMouseClick"),
            ("GameplayLogicScript", "HandleRightMouseClick"),
            ("GameplayLogicScript", "_replayJournal"),
            ("GameplayLogicScript", "_replaySealed"),
            ("GameplayLogicScript", "currentCombatLog"),
            ("GameplayLogicScript", "gsv_CurrentGame"),
            ("GameplayLogicScript", "go_FocusedObject"),
            ("GameplayLogicScript", "go_ReturnToMainMenu"),
            ("GameplayLogicScript", "go_DownloadLog"),
            ("GameplayLogicScript", "go_ReportBug"),
            ("GameplayLogicScript", "go_RequestCancelBtn"),
            ("GameplayLogicScript", "go_ChoiceButton1"),
            ("GameplayLogicScript", "go_YouWin"),
            ("GameplayLogicScript", "go_LogView"),
            ("GameplayLogicScript", "go_ChatBox"),
            ("GameplayLogicScript", "Lps_Players"),
            ("GameplayLogicScript", "card_Database"),
            ("GameplayLogicScript", "ShuffleDeck"),
            ("GameplayLogicScript", "ShuffleWithString"),
            ("GameplayLogicScript", "ShuffleHandIntoDeck"),
            ("CardDatabaseScript", "GetCardImage"),
            ("PlayerState", "Lgo_MyDeploy"),
            ("GameplayLogicScript", "go_P0Board"),
            ("GameplayLogicScript", "e_GameStyle"),
            ("GameplayLogicScript", "eMultiStyle"),
            ("GameplayLogicScript", "isPrivate"),
            ("GameplayLogicScript", "isTimerLobby"),
            ("GameplayLogicScript", "tls_Translation"),
            ("OPBMatchManager", "ActiveServerMatchNo"),
            ("GameplayLogicScript", "go_P1Board"),
            ("HostJoinScript", "go_Patchnotes"),
            ("HostJoinScript", "go_Sponsor"),
            ("HostJoinScript", "go_Sponsor2"),
            ("HostJoinScript", "go_SponsorButton1"),
            ("HostJoinScript", "go_SponsorButton2"),
            ("HostJoinScript", "go_SponsorButton3"),
            ("HostJoinScript", "go_MatchHistory"),
            ("OPBManager", "toggleButtonText"),
            ("HostJoinScript", "go_IPAddress"),
            ("HostJoinScript", "tmp_IPAddress"),
            ("HostJoinScript", "joinCode"),
            ("HostJoinScript", "roomID"),
            ("ReplayEvidenceJournal", "Path"),
            ("ReplayEvidenceJournal", "Id"),
            ("GameStateVars", "iTurnNumber"),
            // Event Coach
            ("HostJoinScript", "go_Settings"),
            ("HostJoinScript", "go_SettingsCanvas"),
            ("HostJoinScript", "go_SettingsBackButton"),
            ("HostJoinScript", "go_Patreon"),
            ("HostJoinScript", "go_PlayerName"),
            ("HostJoinScript", "go_OPBounty"),
            ("HostJoinScript", "go_DeckSelector"),
            ("HostJoinScript", "go_Multiplayer"),
            ("CardDatabaseScript", "Instance"),
            ("CardDatabaseScript", "dict_Cards"),
            ("CardDefinition", "characterName"),
            ("CardDefinition", "cardColors"),
            ("CardDefinition", "cardType"),
        };

        void Awake()
        {
            Log = Logger;
            Instance = this;
            QuietInPlayerLog();
            Log.LogInfo($"OPTCG Trainer {Version} su Unity {Application.version} / {Application.unityVersion}");
            site = Config.Bind("Site", "Url", "https://optcgtrainer.com", "The OPTCG Trainer website the mod links to and uploads games to.");
            FocusMode = Config.Bind("Game", "FocusMode", false, "Focus mode: during a game, hide everything except the board, the choices, Back to Main and Concede.");
            FocusFullscreen = Config.Bind("Game", "FocusFullscreen", true, "Focus mode also turns the window into full screen (like F11 in a browser). Turning Focus mode off brings the window back.");
            CheckUpdates = Config.Bind("Updates", "Check", true, "When OPTCGSim starts, ask GitHub if there's a newer OPTCG Trainer. If there is, the line above the OPTCG TRAINER button offers it: nothing is downloaded until you click.");
            WindowBeforeFocus = Config.Bind("Game", "WindowBeforeFocus", "", "Used by the mod: the window to go back to when Focus mode is turned off. Leave it empty.");
            HideMultiplayerExtras = Config.Bind("Multiplayer", "HideExtras", true, "Hide the sponsor banner (with its 3 tabs), TCG Match Making (Discord) and Match History on the Multiplayer screen, and the Open OPBounty button at the top right. Play Ranked / Launch OPBounty and everything else stay.");
            PasteOPBountyCode = Config.Bind("Multiplayer", "PasteOPBountyCode", true, "When the OPBounty program copies a room code for you (PLEASE PASTE LOBBY CODE), put it in the Join field of the Private screen by itself. You still press Join.");
            DevCommands.Init();
            // prima di tutto il resto: se una versione nuova del sim rompe la mod, l'aggiornamento che la ripara si trova lo stesso
            try { Updater.Init(); } catch (Exception e) { Log.LogWarning("aggiornamenti: " + e.Message); }
            CheckHooks();
            LoadLogo();
            Patch();
            try { Match.RecoverLeftovers(); } catch (Exception e) { Log.LogWarning("recupero delle partite: " + e.Message); }
            Account.Init();
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (MenuButton.Instance == null) StartCoroutine(MenuButton.AddWhenReady());
            };
        }

        // BepInEx copia ogni sua riga di log anche nel Player.log del sim, e il programma OPBounty legge proprio quel file per
        // capire la stanza creata, chi è entrato e chi ha vinto (docs/ricerca-opbounty.md). Da qui in poi le righe della mod
        // vanno solo nel file di BepInEx (LogOutput.log nella cartella di BepInEx): per OPBounty il log è quello di un sim senza mod.
        static void QuietInPlayerLog()
        {
            try
            {
                foreach (var listener in BepInEx.Logging.Logger.Listeners.OfType<UnityLogListener>().ToList())
                    BepInEx.Logging.Logger.Listeners.Remove(listener);
            }
            catch (Exception e) { Log.LogWarning("le righe della mod restano anche nel Player.log: " + e.Message); }
        }

        // un aggancio alla volta: se uno non va (versione nuova del sim), gli altri funzionano lo stesso e il sim non si ferma
        static void Patch()
        {
            var harmony = new Harmony(Guid);
            Type[] types;
            // se un tipo della mod non si carica (un tipo di Batsu sparito), restano tutti gli altri
            try { types = typeof(Plugin).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { missingHooks++; types = e.Types.Where(t => t != null).ToArray(); Log.LogError("tipi della mod non caricati: " + e.Message); }
            foreach (var type in types.Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            {
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e)
                {
                    missingHooks++;
                    Log.LogError($"aggancio {type.Name} non applicato: {e.Message}");
                }
            }
        }

        void Update()
        {
            for (int i = 0; i < Ticks.Length; i++)
            {
                try { Ticks[i].tick(); }
                catch (Exception e)
                {
                    // un errore della mod non deve mai fermare il sim: si scrive nel log (la prima volta, poi ogni tanto) e si va avanti
                    var text = e.ToString();
                    if (text != lastError[i] || Time.frameCount % 600 == 0) Log.LogError(Ticks[i].name + ": " + text);
                    lastError[i] = text;
                }
            }
        }

        static void CheckHooks()
        {
            var game = typeof(GameplayLogicScript).Assembly;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var (typeName, member) in Hooks)
            {
                var type = game.GetType(typeName);
                bool found = type != null && type.GetMember(member, all).Length > 0;
                if (!found)
                {
                    missingHooks++;
                    Log.LogError($"aggancio {typeName}.{member}: MANCA");
                }
            }
            Log.LogInfo(missingHooks == 0 ? $"tutti i {Hooks.Length} agganci ci sono" : $"{missingHooks} agganci mancanti");
        }

        static void LoadLogo()
        {
            try
            {
                using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("OPTCGTrainer.logo.png");
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var logo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(logo, bytes);
                LogoSprite = Sprite.Create(logo, new Rect(0, 0, logo.width, logo.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Log.LogWarning("logo non caricato: " + e.Message);
            }
        }

        // Solo se qualcosa non va: dopo un aggiornamento del sim una riga piccola in alto dice che una parte della mod è spenta.
        // Sotto, se su GitHub c'è una versione nuova, la si offre qui: il pulsante OPTCG TRAINER (con la sua riga) può mancare.
        void OnGUI()
        {
            if (missingHooks == 0) return;
            float scale = Screen.height / 1080f;
            warning ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(14 * scale), richText = true };
            var old = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.45f);
            GUI.Label(new Rect(0, 4, Screen.width, 40), $"OPTCG Trainer {Version}: this OPTCGSim version changed, some mod features are off. Check for a mod update.", warning);
            GUI.color = old;
            var update = Updater.Text;
            if (update == null) return;
            var line = new Rect(Screen.width / 2f - 260 * scale, 28 * scale, 520 * scale, 30 * scale);
            updateButton ??= new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * scale), richText = true };
            if (!Updater.Clickable) GUI.Label(line, update, warning);
            else if (GUI.Button(line, update, updateButton)) Updater.Install();
        }
    }
}
