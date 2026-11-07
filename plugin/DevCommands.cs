// Telecomando per le prove: permette di guidare il sim da fuori senza toccare mouse e tastiera.
// È acceso SOLO se esiste il file BepInEx\config\optcgtrainer.dev; per chi installa la mod non esiste.
// Si scrivono comandi in BepInEx\config\optcgtrainer-cmd.txt (uno per riga), le risposte arrivano in optcgtrainer-out.txt:
//   scene                 la scena aperta
//   buttons               i pulsanti visibili: scritta, posizione sullo schermo, nome dell'oggetto
//   click <scritta>       preme il pulsante con quella scritta (uguale, se no che la contiene)
//   fields                i GameObject pubblici di GameplayLogicScript: acceso o spento, dove stanno sullo schermo
//   call <metodo>         chiama un metodo senza argomenti di GameplayLogicScript (es. Concede)
//   press <oggetto>       preme il pulsante sull'oggetto con quel nome (o un suo figlio)
//   type <testo>          scrive nel campo di testo selezionato (come se l'avessi scritto tu)
//   submit                preme Invio nel campo di testo selezionato
//   concede               il giocatore 1 concede come in multiplayer (ConcedeLocal): YOU LOSE e la schermata di fine
//   screen                modo della finestra e misure
//   pcall <metodo> <giocatore> [numeri…]  chiama una funzione del sim (PlayerState, int…), es. "pcall TrashLife_Internal 0 2":
//                         per preparare una situazione nelle prove (Life nel trash, in fondo al mazzo, una carta dal trash in mano…)
//   zones                 mano, trash e fondo del mazzo dei due giocatori, e quello che la mod sa (fondo noto, mano nota)
//   over <x> <y>          il punto (in alto a sinistra, come in "buttons") è sopra la mod? (i clic lì non vanno al tavolo)
#if DEVTOOLS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class DevCommands
    {
        static readonly string Flag = Path.Combine(Paths.ConfigPath, "optcgtrainer.dev");
        static readonly string CmdFile = Path.Combine(Paths.ConfigPath, "optcgtrainer-cmd.txt");
        static readonly string OutFile = Path.Combine(Paths.ConfigPath, "optcgtrainer-out.txt");
        internal static bool Enabled;
        static float next;

        internal static void Init()
        {
            Enabled = File.Exists(Flag);
            if (!Enabled) return;
            Plugin.Log.LogWarning("telecomando delle prove ACCESO (" + Flag + ")");
            // nelle prove il sim gira anche dietro le altre finestre (Unity normalmente si mette in pausa senza il primo piano)
            Application.runInBackground = true;
        }

        internal static void Tick()
        {
            if (!Enabled || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            if (!File.Exists(CmdFile)) return;
            string[] lines;
            try { lines = File.ReadAllLines(CmdFile); File.Delete(CmdFile); }
            catch (IOException) { return; } // il file si sta ancora scrivendo: al prossimo giro
            var out_ = new StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line == "") continue;
                out_.AppendLine("> " + line);
                try { out_.AppendLine(Run(line)); }
                catch (Exception e) { out_.AppendLine("ERRORE " + e); }
            }
            File.AppendAllText(OutFile, out_.ToString());
        }

        static string Run(string line)
        {
            int sp = line.IndexOf(' ');
            string cmd = sp < 0 ? line : line.Substring(0, sp), arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "scene": return SceneManager.GetActiveScene().name;
                case "buttons": return Buttons();
                case "click": return Click(arg);
                case "fields": return Fields();
                case "call": return Call(arg);
                case "press": return Press(arg);
                case "type": return TypeText(arg, false);
                case "submit": return TypeText(null, true);
                case "concede": return Concede();
                case "pcall": return PCall(arg);
                case "screen": return $"{Screen.fullScreenMode} {Screen.width}x{Screen.height}";
                case "zones": return Zones();
                case "over":
                    var xy = arg.Split(' ');
                    return GameUi.Over(new Vector2(float.Parse(xy[0]), Screen.height - float.Parse(xy[1]))) ? "sopra la mod" : "fuori";
                default: return "comando sconosciuto";
            }
        }

        static string Label(Component c)
        {
            var tmp = c.GetComponentInChildren<TMP_Text>();
            if (tmp != null) return tmp.text.Replace("\n", " ").Trim();
            var text = c.GetComponentInChildren<Text>();
            return text != null ? text.text.Replace("\n", " ").Trim() : "";
        }

        static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            // in alto a sinistra, come le foto
            return new Rect(a.x, Screen.height - b.y, b.x - a.x, b.y - a.y);
        }

        static string Where(GameObject go)
        {
            var rt = go.transform as RectTransform;
            if (rt == null) return "(non UI)";
            var r = ScreenRect(rt);
            return $"x={r.x:0} y={r.y:0} w={r.width:0} h={r.height:0}";
        }

        static string Buttons()
        {
            var sb = new StringBuilder($"schermo {Screen.width}x{Screen.height}\n");
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled))
                sb.AppendLine($"[{Label(b)}] {Where(b.gameObject)} {(b.interactable ? "" : "(spento) ")}<{b.gameObject.name}>");
            return sb.ToString();
        }

        static string Click(string label)
        {
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled && b.interactable).ToList();
            var b = all.FirstOrDefault(x => string.Equals(Label(x), label, StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(x => Label(x).IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
            if (b == null) return "nessun pulsante con questa scritta";
            b.onClick.Invoke();
            return "premuto [" + Label(b) + "] <" + b.gameObject.name + ">";
        }

        static string Press(string name)
        {
            var go = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.isActiveAndEnabled && (b.gameObject.name == name || b.transform.parent != null && b.transform.parent.name == name));
            if (go == null) return "nessun pulsante con questo nome";
            go.onClick.Invoke();
            return "premuto <" + go.gameObject.name + ">";
        }

        static string TypeText(string text, bool submit)
        {
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            if (field == null) return "nessun campo di testo selezionato";
            if (text != null) field.text = text;
            if (submit) field.onSubmit.Invoke(field.text);
            return "ok <" + selected.name + ">: " + field.text;
        }

        static string Zones()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            var sb = new StringBuilder();
            for (int p = 0; p < game.Lps_Players.Count; p++)
            {
                var ps = game.Lps_Players[p];
                string L(System.Collections.Generic.List<GameObject> l) => string.Join(" ", l.Select(Knowledge.Id));
                sb.AppendLine($"P{p} mano: {L(ps.Lgo_MyHand)}");
                sb.AppendLine($"P{p} trash: {L(ps.Lgo_MyTrash)}");
                sb.AppendLine($"P{p} fondo del mazzo (i primi 5): {L(ps.Lgo_MyDeck.Take(5).ToList())}");
            }
            sb.AppendLine("fondo noto (mio): " + string.Join(" ", Knowledge.KnownBottom(game.Lps_Players[0].Lgo_MyDeck).Select(Knowledge.Id)));
            sb.AppendLine("note in mano all'avversario: " + string.Join(" ", Knowledge.OppKnownHand(game.Lps_Players[1]).Select(Knowledge.Id)));
            return sb.ToString();
        }

        static string PCall(string arg)
        {
            var parts = arg.Split(' ');
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            var m = typeof(GameplayLogicScript).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == parts[0] && x.GetParameters().Any(p => p.ParameterType == typeof(PlayerState)));
            if (m == null) return "metodo non trovato";
            // il giocatore va nel parametro PlayerState (ovunque sia), i numeri dopo negli altri, in ordine
            var ps = m.GetParameters();
            var values = new object[ps.Length];
            int next = 2;
            for (int i = 0; i < ps.Length; i++)
                values[i] = ps[i].ParameterType == typeof(PlayerState) ? game.Lps_Players[int.Parse(parts[1])]
                    : next < parts.Length ? Convert.ChangeType(parts[next++], ps[i].ParameterType) : ps[i].DefaultValue;
            m.Invoke(game, values);
            return "chiamato " + m.Name;
        }

        static string Concede()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            if (game == null) return "GameplayLogicScript non c'è in questa scena";
            var players = (System.Collections.IList)HarmonyLib.AccessTools.Field(typeof(GameplayLogicScript), "Lps_Players").GetValue(game);
            HarmonyLib.AccessTools.Method(typeof(GameplayLogicScript), "ConcedeLocal").Invoke(game, new[] { players[0] });
            return "conceduto";
        }

        static string Fields()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            if (game == null) return "GameplayLogicScript non c'è in questa scena";
            var sb = new StringBuilder($"schermo {Screen.width}x{Screen.height}\n");
            foreach (var f in typeof(GameplayLogicScript).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => f.FieldType == typeof(GameObject)))
            {
                var go = f.GetValue(game) as GameObject;
                if (go == null) { sb.AppendLine(f.Name + ": null"); continue; }
                sb.AppendLine($"{f.Name}: {(go.activeInHierarchy ? "ACCESO" : "spento")} {Where(go)} \"{Label(go.transform).Replace("\"", "'")}\" <{go.name}>");
            }
            return sb.ToString();
        }

        static string Call(string method)
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            if (game == null) return "GameplayLogicScript non c'è in questa scena";
            var m = typeof(GameplayLogicScript).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m == null) return "metodo non trovato";
            m.Invoke(game, null);
            return "chiamato " + method;
        }
    }
}
#else
namespace OPTCGTrainer
{
    // nella versione che si installa (pacchetto, -p:DevTools=false) il telecomando delle prove non c'è proprio
    static class DevCommands
    {
        internal static bool Enabled => false;
        internal static void Init() { }
        internal static void Tick() { }
    }
}
#endif
