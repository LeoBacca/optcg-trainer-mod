// OPTCG Trainer — mod per OPTCGSim. Porta OPTCG Trainer dentro il sim:
//   menu principale  il pulsante OPTCG TRAINER (MenuButton.cs)
//   in partita       Add note, Focus mode, a fine partita "apri su OPTCG Trainer" (GameUi.cs, Match.cs)
//   sito             collegamento dell'account e caricamento di log e note (Account.cs, docs/api-sito.md)
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
        public const string Version = "0.3.2";
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static Sprite LogoSprite;
        internal static ConfigEntry<bool> FocusMode, FocusFullscreen;
        internal static ConfigEntry<string> WindowBeforeFocus;
        static ConfigEntry<string> site;
        internal static string SiteUrl => site.Value;
        static int missingHooks;
        GUIStyle warning;
        string lastError;

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
            ("GameplayLogicScript", "tls_Translation"),
            ("OPBMatchManager", "ActiveServerMatchNo"),
            ("GameplayLogicScript", "go_P1Board"),
            ("HostJoinScript", "go_Patchnotes"),
            ("ReplayEvidenceJournal", "Path"),
            ("ReplayEvidenceJournal", "Id"),
            ("GameStateVars", "iTurnNumber"),
        };

        void Awake()
        {
            Log = Logger;
            Instance = this;
            Log.LogInfo($"OPTCG Trainer {Version} su Unity {Application.version} / {Application.unityVersion}");
            site = Config.Bind("Site", "Url", "https://optcgtrainer.com", "The OPTCG Trainer website the mod links to and uploads games to.");
            FocusMode = Config.Bind("Game", "FocusMode", false, "Focus mode: during a game, hide everything except the board, the choices, Back to Main and Concede.");
            FocusFullscreen = Config.Bind("Game", "FocusFullscreen", true, "Focus mode also turns the window into full screen (like F11 in a browser). Turning Focus mode off brings the window back.");
            WindowBeforeFocus = Config.Bind("Game", "WindowBeforeFocus", "", "Used by the mod: the window to go back to when Focus mode is turned off. Leave it empty.");
            CheckHooks();
            LoadLogo();
            DevCommands.Init();
            Patch();
            try { Match.RecoverLeftovers(); } catch (Exception e) { Log.LogWarning("recupero delle partite: " + e.Message); }
            Account.Init();
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (MenuButton.Instance == null) StartCoroutine(MenuButton.AddWhenReady());
            };
        }

        // un aggancio alla volta: se uno non va (versione nuova del sim), gli altri funzionano lo stesso e il sim non si ferma
        static void Patch()
        {
            var harmony = new Harmony(Guid);
            foreach (var type in typeof(Plugin).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
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
            try
            {
                MenuButton.FollowSource();
                GameUi.Tick();
                DevCommands.Tick();
            }
            catch (Exception e)
            {
                // un errore della mod non deve mai fermare il sim: si scrive nel log (la prima volta, poi ogni tanto) e si va avanti
                var text = e.ToString();
                if (text != lastError || Time.frameCount % 600 == 0) Log.LogError(e);
                lastError = text;
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
        void OnGUI()
        {
            if (missingHooks == 0) return;
            warning ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(14 * Screen.height / 1080f) };
            var old = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.45f);
            GUI.Label(new Rect(0, 4, Screen.width, 40), $"OPTCG Trainer {Version}: this OPTCGSim version changed, some mod features are off. Check for a mod update.", warning);
            GUI.color = old;
        }
    }
}
