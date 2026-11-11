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
//   preview <id>|off      tiene aperta la carta grande (come col mouse sopra una carta), es. "preview OP18-027"
//   choice <n> <tipo>     accende il pulsante di scelta n (1-4) del sim, es. "choice 3 BackToMain"; "choice off" li spegne
//   hit <x> <y>           chi prenderebbe un clic vero del mouse in quel punto (in alto a sinistra, come in "buttons")
//   coachui <today|stats|setup|close>  apre (o chiude) la pagina dell'Event Coach su quella scheda
//   tap <x> <y>           un clic vero in quel punto (in alto a sinistra, come "buttons"): va a chi lo prenderebbe col mouse
//   hover <x> <y>         il mouse entra in quel punto (per i riquadri che seguono il mouse); "hover off" esce
//   mats                  quanti materiali ci sono in memoria (per vedere che le pagine rifatte non li accumulano)
//   fonts                 i font TextMeshPro caricati (e se hanno un grassetto vero)
//   kids <nome>           i figli dell'oggetto con quel nome (anche spenti): acceso o no, componenti, dove stanno
//   canvases              tutti i canvas: modo, ordine, attivo, percorso
//   coach                 Event Coach: lo stato calcolato (evento, oggi X/Y, numero di oggi, serie, pass, statistiche, giorni)
//   coach set k=v; k=v    un evento di prova: name, date (yyyy-MM-dd), ready, start, goal, rest (date o giorni: sun,sat),
//                         format, leader (id della carta), count (ladder,queue,private,solo). Es. "coach set date=2026-11-21; goal=40"
//   coach now <yyyy-MM-dd HH:mm>|off   un'ora del PC finta, solo per il coach (cambio del giorno alle 4, giorni saltati)
//   coach games           tutte le partite che il coach conosce, con il perché non contano
//   coach fake <giorni> <al giorno> [esiti di oggi] | off   partite finte solo in memoria (mai salvate), per vedere le schermate
//                         piene; gli esiti di oggi una lettera per partita, es. "coach fake 6 4 wol" (o = senza esito)
//   coach stop            ferma l'evento e lo mette in archivio; "coach archive" elenca l'archivio
#if DEVTOOLS
using System;
using System.Globalization;
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

        static string previewId;

        internal static void Tick()
        {
            if (!Enabled) return;
            // il sim spegne la carta grande appena il mouse passa sopra una carta: si rimette a ogni fotogramma
            if (previewId != null)
            {
                var g = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
                if (g != null) g.s_FocusedChatPreview = previewId;
            }
            if (Time.realtimeSinceStartup < next) return;
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
                case "formats":
                    var g = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
                    return string.Join("\n", ((MultiplayerStyle[])Enum.GetValues(typeof(MultiplayerStyle))).Select(st => st + " = " + Match.FormatName(g, st)));
                case "hit": return Hit(arg);
                case "over":
                    var xy = arg.Split(' ');
                    return GameUi.Over(new Vector2(float.Parse(xy[0]), Screen.height - float.Parse(xy[1]))) ? "sopra la mod" : "fuori";
                case "preview":
                    previewId = arg == "off" ? null : arg;
                    if (previewId == null) { var gp = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>(); if (gp != null) gp.s_FocusedChatPreview = ""; }
                    return previewId == null ? "carta grande spenta" : "carta grande: " + previewId;
                case "choice": return Choice(arg);
                case "tree": return Tree(arg);
                case "coach": return CoachCmd(arg);
                case "kids": return Kids(arg);
                case "coachui":
                    if (arg == "close") { CoachUi.Close(); return "pagina chiusa"; }
                    CoachUi.Open(arg);
                    return CoachUi.IsOpen ? "pagina aperta: " + arg : "pagina non aperta (il menu non è pronto?)";
                case "tap": return Tap(arg, false);
                case "mats": return "materiali in memoria: " + Resources.FindObjectsOfTypeAll<Material>().Length;
                case "fonts":
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Select(f => $"{f.name} · bold={(f.fontWeightTable != null && f.fontWeightTable.Length > 7 && f.fontWeightTable[7].regularTypeface != null ? f.fontWeightTable[7].regularTypeface.name : "-")} boldSpacing={f.boldSpacing}"));
                case "hover": return Tap(arg, true);
                case "canvases": return Canvases();
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

        // come fa Unity con un clic vero: l'oggetto più in alto sotto il punto e chi riceve il clic risalendo dai genitori
        static string Hit(string arg)
        {
            var xy = arg.Split(' ');
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return "nessun EventSystem";
            var data = new UnityEngine.EventSystems.PointerEventData(events) { position = new Vector2(float.Parse(xy[0]), Screen.height - float.Parse(xy[1])) };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(data, hits);
            if (hits.Count == 0) return "niente";
            var top = hits[0].gameObject;
            var handler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(top);
            return $"sopra <{top.name}>, il clic va a <{(handler != null ? handler.name : "nessuno")}>";
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
            var pv = game.img_CardPreview;
            if (pv != null) sb.AppendLine($"img_CardPreview: {(pv.enabled && pv.gameObject.activeInHierarchy ? "ACCESO" : "spento")} {Where(pv.gameObject)}");
            return sb.ToString();
        }

        // dove sta un oggetto nella gerarchia: genitori, posto tra i fratelli, canvas con il loro ordine (chi è disegnato sopra a chi)
        static string Tree(string name)
        {
            var go = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == name)?.gameObject;
            if (go == null) return "nessun oggetto con questo nome";
            var sb = new StringBuilder();
            for (var t = go.transform; t != null; t = t.parent)
            {
                var c = t.GetComponent<Canvas>();
                sb.AppendLine($"{t.name} #{t.GetSiblingIndex()}/{(t.parent != null ? t.parent.childCount : 0)}" + (c != null ? $" canvas override={c.overrideSorting} layer={c.sortingLayerName} order={c.sortingOrder}" : ""));
            }
            return sb.ToString();
        }

        static GameObject hovered;

        // come il mouse: l'oggetto più in alto sotto il punto, e il clic (o l'entrata) a chi lo prenderebbe
        static string Tap(string arg, bool hover)
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return "nessun EventSystem";
            var data = new UnityEngine.EventSystems.PointerEventData(events) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            if (hover && hovered != null) { UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hovered, data, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler); hovered = null; }
            if (arg == "off") return "mouse fuori";
            var xy = arg.Split(' ');
            data.position = new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), Screen.height - float.Parse(xy[1], CultureInfo.InvariantCulture));
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(data, hits);
            if (hits.Count == 0) return "niente";
            var top = hits[0].gameObject;
            data.pointerCurrentRaycast = hits[0];
            data.pointerPress = top;
            if (hover)
            {
                hovered = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(top, data, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                return $"mouse sopra <{top.name}> → <{(hovered != null ? hovered.name : "nessuno")}>";
            }
            var got = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(top, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            if (got == null) { got = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(top, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler); }
            return $"clic su <{top.name}> → <{(got != null ? got.name : "nessuno")}>";
        }

        static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        static string Kids(string name)
        {
            var go = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == name)?.gameObject;
            if (go == null) return "nessun oggetto con questo nome";
            var sb = new StringBuilder(PathOf(go.transform) + (go.activeInHierarchy ? " ACCESO " : " spento ") + Where(go) + "\n");
            foreach (Transform c in go.transform)
            {
                var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name));
                sb.AppendLine($"  {c.name} {(c.gameObject.activeSelf ? "on" : "off")} {Where(c.gameObject)} [{comps}] \"{Label(c).Replace("\"", "'")}\" figli={c.childCount}");
            }
            return sb.ToString();
        }

        static string Canvases()
        {
            var sb = new StringBuilder();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(c => PathOf(c.transform)))
                sb.AppendLine($"{PathOf(c.transform)} {(c.gameObject.activeInHierarchy ? "ACCESO" : "spento")} {(c.isRootCanvas ? "radice " + c.renderMode : "annidato")} override={c.overrideSorting} layer={c.sortingLayerName} order={c.sortingOrder} cam={(c.worldCamera != null ? c.worldCamera.name : "-")}");
            return sb.ToString();
        }

        static string Choice(string arg)
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            if (game == null) return "GameplayLogicScript non c'è in questa scena";
            if (arg == "off")
            {
                foreach (var b in new[] { game.go_ChoiceButton1, game.go_ChoiceButton2, game.go_ChoiceButton3, game.go_ChoiceButton4 }) b.SetActive(false);
                return "scelte spente";
            }
            var parts = arg.Split(' ');
            game.AddChoice(int.Parse(parts[0]), (ButtonChoiceType)Enum.Parse(typeof(ButtonChoiceType), parts[1]));
            return "scelta " + parts[0] + " accesa";
        }

        // ---- Event Coach (fase 1: niente schermate, i numeri si guardano da qui) ----

        static readonly string[] WeekDays = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        static string CoachCmd(string arg)
        {
            int sp = arg.IndexOf(' ');
            string sub = sp < 0 ? arg : arg.Substring(0, sp), rest = sp < 0 ? "" : arg.Substring(sp + 1).Trim();
            switch (sub)
            {
                case "": CoachStore.WaitReady(15); return CoachText(CoachStore.State());
                case "set": return CoachSet(rest);
                case "now":
                    if (rest == "off") CoachStore.FakeNow = null;
                    else CoachStore.FakeNow = DateTime.ParseExact(rest, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    return "ora del coach: " + CoachStore.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (CoachStore.FakeNow != null ? " (finta)" : " (vera)");
                case "games":
                    CoachStore.WaitReady(15);
                    var st = CoachStore.State();
                    var games = st?.games ?? CoachStore.Games();
                    var sb = new StringBuilder($"{games.Count} partite (dai log: {games.Sum(g => g.ids.Count)})\n");
                    foreach (var g in games) sb.AppendLine(Row(g, true));
                    return sb.ToString();
                case "fake":
                    CoachStore.FakeGames.Clear();
                    CoachStore.Version++;
                    if (rest == "off") return "partite finte tolte";
                    var ev = CoachStore.Data.active;
                    if (ev == null) return "nessun evento attivo";
                    var nums = rest.Split(' ');
                    int days = int.Parse(nums[0], CultureInfo.InvariantCulture), perDay = nums.Length > 1 ? int.Parse(nums[1], CultureInfo.InvariantCulture) : 5;
                    string[] opps = { "OP01-060", "OP02-001", "OP03-001", "OP04-001", "OP05-001", "OP06-001", "OP07-001", "OP08-001", "OP09-001", "OP10-001", "ST01-001", "OP01-001" };
                    var rnd = new System.Random(7);
                    var today = Coach.DayOf(CoachStore.Now);
                    for (int d = days; d >= 1; d--)
                    {
                        var day = today.AddDays(-d);
                        int n = rnd.Next(0, perDay * 3 / 2 + 1);
                        for (int i = 0; i < n; i++)
                        {
                            var start = DateTime.SpecifyKind(day.AddHours(16 + i * 0.5), DateTimeKind.Local).ToUniversalTime();
                            double r = rnd.NextDouble();
                            var result = r < 0.05 ? "o" : r < 0.55 ? "w" : "l";
                            var opp = opps[Math.Min(opps.Length - 1, (int)(Math.Abs(rnd.NextDouble() * rnd.NextDouble()) * opps.Length * 1.3))];
                            CoachStore.FakeGames.Add(new GameSummary
                            {
                                v = GameSummary.Current, id = "fake" + d + "x" + i, logName = "fake.log", startedAt = Coach.Iso(start), endedAt = Coach.Iso(start.AddMinutes(12 + rnd.Next(20))),
                                day = Coach.Key(day), result = result, reason = result == "o" ? "unfinished" : result == "w" ? "win" : "loss", ended = result != "o",
                                myLeader = rnd.NextDouble() < 0.85 ? (ev.leader ?? "OP13-001") : "OP01-001", oppLeader = opp, first = rnd.Next(1, 3),
                                turns = rnd.Next(2, 15), kind = "queue", format = ev.format,
                            });
                        }
                    }
                    // il terzo pezzo: gli esiti delle partite finte di oggi, una lettera per partita ("wol" = vinta, incerta, persa)
                    for (int i = 0; nums.Length > 2 && i < nums[2].Length; i++)
                    {
                        var start = CoachStore.Now.AddMinutes(-30 * (nums[2].Length - i)).ToUniversalTime();
                        var result = nums[2][i].ToString();
                        CoachStore.FakeGames.Add(new GameSummary
                        {
                            v = GameSummary.Current, id = "fakeToday" + i, logName = "fake.log", startedAt = Coach.Iso(start), endedAt = Coach.Iso(start.AddMinutes(18)),
                            day = Coach.Key(today), result = result, reason = result == "o" ? "disconnect" : result == "w" ? "win" : "loss", ended = result != "o",
                            myLeader = ev.leader ?? "OP13-001", oppLeader = opps[i % opps.Length], first = 1 + i % 2, turns = 6 + i, kind = "queue", format = ev.format,
                        });
                    }
                    return $"{CoachStore.FakeGames.Count} partite finte negli ultimi {days} giorni";
                case "stop":
                    var done = CoachStore.Stop();
                    return done == null ? "nessun evento attivo" : $"in archivio: {done.ev.name} {done.games}/{done.ev.goal}, serie migliore {done.bestStreak}, livello {done.level}";
                case "archive":
                    var list = string.Join("\n", CoachStore.Data.archive.Select(a =>
                        $"{a.ev.name} ({a.ev.eventDate}): {a.games}/{a.ev.goal}, {a.wins}-{a.losses}, serie migliore {a.bestStreak}, livello {a.level}, fermato {a.archivedAt}"));
                    return list != "" ? list : "archivio vuoto";
                default: return "coach: sottocomando sconosciuto";
            }
        }

        static string CoachSet(string args)
        {
            var today = Coach.DayOf(CoachStore.Now);
            var ev = new CoachEvent { name = "Test event", goal = 150, start = Coach.Key(today) };
            string rest = null;
            foreach (var part in args.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string k = part.Substring(0, eq).Trim().ToLowerInvariant(), v = part.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "name": ev.name = v; break;
                    case "date": ev.eventDate = v; break;
                    case "ready": ev.readyBy = v; break;
                    case "start": ev.start = v; break;
                    case "goal": ev.goal = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case "rest": rest = v; break;
                    case "format": ev.format = v == "" ? null : v; break;
                    case "leader": ev.leader = v == "" ? null : v; break;
                    case "count":
                        var kinds = v.ToLowerInvariant().Split(',').Select(x => x.Trim()).ToArray();
                        ev.ladder = kinds.Contains("ladder");
                        ev.queue = kinds.Contains("queue");
                        ev.privateGames = kinds.Contains("private");
                        ev.solo = kinds.Contains("solo");
                        break;
                    default: return "chiave sconosciuta: " + k;
                }
            }
            if (!(Coach.Day(ev.eventDate) is DateTime eventDay)) return "manca date=yyyy-MM-dd";
            ev.readyBy ??= Coach.Key(eventDay.AddDays(-2));
            // i riposi: date, o giorni della settimana (tutti quelli dal primo giorno al giorno prima dell'evento)
            foreach (var token in (rest ?? "").Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x != ""))
            {
                int wd = Array.IndexOf(WeekDays, token.Length >= 3 ? token.Substring(0, 3) : token);
                if (Coach.Day(token) is DateTime d) ev.restDays.Add(Coach.Key(d));
                else if (wd >= 0 && Coach.Day(ev.start) is DateTime from)
                {
                    for (var x = from; x < eventDay; x = x.AddDays(1))
                        if ((int)x.DayOfWeek == wd) ev.restDays.Add(Coach.Key(x));
                }
                else return "riposo non capito: " + token;
            }
            var problem = CoachStore.Start(ev);
            CoachStore.WaitReady(15);
            return problem != null ? "evento non impostato: " + problem : "evento impostato\n" + CoachText(CoachStore.State());
        }

        static string Day(DateTime d) => d.ToString("ddd dd MMM", CultureInfo.InvariantCulture);
        static string Rec(Record r) => $"{r.games} ({r.wins}-{r.losses}{(r.Percent != null ? $", {r.Percent}%" : "")}{(r.Unknown > 0 ? $", {r.Unknown} incerte" : "")})";
        static string Leader(string id, string name) => id == null ? "?" : name != null ? name + " " + id : id;

        static string Row(CoachGame g, bool date = false) =>
            $"  {g.startLocal.ToString(date ? "dd/MM HH:mm" : "HH:mm", CultureInfo.InvariantCulture)} {(g.result == "w" ? "W" : g.result == "l" ? "L" : "–")}"
            + $" vs {Leader(g.oppLeader, g.oppLeaderName)} · T{g.turns} · {(g.minutes != null ? g.minutes.Value.ToString("0", CultureInfo.InvariantCulture) + " min" : "? min")}"
            + $" · {(g.first == 1 ? "primo" : g.first == 2 ? "secondo" : "?")} · {g.Kind ?? "multiplayer"}{(g.format != null ? "/" + g.format : "")} · {g.reason}"
            + $" · {(g.why == null ? "CONTA" : g.why + ", non conta")}{(g.ids.Count > 1 ? $" · {g.ids.Count} pezzi" : "")}{(g.imported ? " · da CombatLogs" : "")}";

        static string Status(CoachDay d) =>
            (d.eventDay ? "EVENTO" : d.rest ? "riposo" : d.future ? "da fare" : d.isToday ? (d.Met ? "fatto (oggi)" : "oggi, in corso")
                : d.target == 0 ? "niente da fare" : d.Met ? "fatto" : "mancato") + (d.readyBy ? " · READY BY" : "");

        static string CoachText(CoachState st)
        {
            if (st == null) return "nessun evento attivo (coach set name=…; date=yyyy-MM-dd; goal=…)";
            var ev = st.ev;
            var sb = new StringBuilder();
            var kinds = string.Join(", ", new[] { ev.ladder ? "ladder" : null, ev.queue ? "queue" : null, ev.privateGames ? "private" : null, ev.solo ? "solo" : null }.Where(x => x != null));
            sb.AppendLine($"EVENTO {ev.name} · {Day(st.eventDay)} · ready by {Day(st.readyBy)} · obiettivo {ev.goal} · formato {ev.format ?? "tutti"} · leader {ev.leader ?? "-"} · contano {kinds}, da T{Coach.MinTurns}");
            sb.AppendLine($"piano: dal {Day(st.start)} · {st.playDays} giorni di gioco · {st.restDays} di riposo · primo numero {st.firstTarget} al giorno");
            sb.AppendLine($"ora del PC {st.now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{(CoachStore.FakeNow != null ? " (finta)" : "")} → giorno del coach {Day(st.today)}, finisce {st.dayEnds.ToString("ddd dd MMM HH:mm", CultureInfo.InvariantCulture)}"
                + (st.beforeStart ? " · IL PIANO NON È COMINCIATO" : "") + (st.over ? " · EVENTO PASSATO" : ""));
            sb.AppendLine($"OGGI {st.todayCount}/{st.todayTarget}{(st.restToday ? " (riposo)" : "")} · ne mancano {st.todayLeft} · numero di adesso {st.rate}"
                + (st.warning ? $" · AVVISO: troppo alto (più del doppio di {st.firstTarget})" : ""));
            sb.AppendLine($"evento fra {st.daysToEvent} giorni · ready by fra {st.daysToReadyBy} ({st.playDaysLeft} giorni di gioco) · partite {st.total}/{st.goal}, mancano {st.toGo}"
                + (st.behind ? " · IN RITARDO" : "") + (st.goalReached ? " · OBIETTIVO RAGGIUNTO" : ""));
            sb.AppendLine($"SERIE {st.streak} (migliore {st.bestStreak}) · distintivi " + string.Join(", ", Coach.StreakBadges.Select(b => b + (st.Badge(b) ? " sì" : $" no (mancano {Math.Max(0, b - st.streak)})"))));
            sb.AppendLine($"PASS livello {st.level}/{Coach.Levels}{(st.title != null ? " " + st.title : "")} · prossimo a {st.nextLevelAt} partite (mancano {st.toNextLevel})"
                + $" · titoli a {Coach.GamesForLevel(5, ev.goal)} bronze, {Coach.GamesForLevel(10, ev.goal)} silver, {Coach.GamesForLevel(15, ev.goal)} gold, {ev.goal} EVENT READY");
            sb.AppendLine($"STATISTICHE partite {Rec(st.all)} · primo {Rec(st.goingFirst)} · secondo {Rec(st.goingSecond)} · {st.totalMinutes.ToString("0", CultureInfo.InvariantCulture)} min giocati"
                + $", media {(st.avgMinutes != null ? st.avgMinutes.Value.ToString("0.0", CultureInfo.InvariantCulture) + " min" : "?")}");
            if (ev.leader != null)
                sb.AppendLine($"con {Leader(ev.leader, st.leaderName)}: {st.withLeader.games} di {st.total} · oggi {st.withLeaderToday.games} di {st.todayRecord.games} ({st.withLeaderToday.wins}-{st.withLeaderToday.losses})");
            sb.AppendLine("MATCHUP" + (ev.leader != null ? " con il tuo leader" : "") + ":");
            foreach (var m in st.matchupsWithLeader)
                sb.AppendLine($"  vs {Leader(m.leader, m.name)}: {m.record.games} · {m.record.wins}-{m.record.losses} · {(m.Few ? "–" : m.record.Percent + "%")}");
            sb.AppendLine($"PARTITE DI OGGI ({st.todayGames.Count}):");
            foreach (var g in st.todayGames) sb.AppendLine(Row(g));
            sb.AppendLine("GIORNI:");
            foreach (var d in st.days)
                sb.AppendLine($"  {Day(d.date)}  {d.record.games} ({d.record.wins}-{d.record.losses}) · numero {d.target} · {Status(d)}");
            sb.AppendLine("SETTIMANE: " + string.Join(" · ", st.weeks.Select(w => $"{w.monday.ToString("dd MMM", CultureInfo.InvariantCulture)} {w.games}/{w.plan}{(w.current ? " (questa)" : "")}")));
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
