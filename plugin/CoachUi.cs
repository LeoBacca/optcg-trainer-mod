// Event Coach, le schermate (fase 2; i numeri li fa Coach.cs, i file CoachStore.cs). Disegni approvati: docs/coach-disegni.
//   Settings       a sinistra, sopra Resolution, il pulsante blu EVENT COACH: apre il Setup (è lo "sblocco": finché non lo usi
//                  il coach non si vede da nessuna parte)
//   menu           con un evento attivo, EVENT COACH sotto Settings (a specchio di Become a Patron) con la riga
//                  "Regionals Milan · 12 days · 3/6 today": apre Today
//   Private        con un evento attivo, il riquadro "Today 3/6" dove c'era il banner degli sponsor (solo con la pulizia accesa)
//   fine partita   una riga sotto il riquadro della mod: "Event Coach: 3/6 today", o perché la partita non conta (GameUi)
// Le schermate (Today, Stats, Setup) stanno in una pagina tutta nostra sopra a tutto, con lo sfondo di legno di Batsu:
// il menu di Batsu sotto resta com'è e non riceve i clic. Back (o Esc) la chiude.
// Lo stato si ricalcola quando si apre una schermata, quando finisce una partita e al più ogni 30 secondi (mai a ogni fotogramma).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static partial class CoachUi
    {
        static HostJoinScript host;
        static float nextLook;
        static GameObject root, menuButton, settingsButton, privateBox;
        static RectTransform page;
        static TextMeshProUGUI menuStatus, settingsStatus;
        static string tab = "today";
        static bool built, settingsShown;

        // ---- lo stato, ricalcolato quando serve ----
        static CoachState state;
        static float stateAt = -999;
        static bool dirty = true;
        static int version, privateDrawn = -1, storeSeen = -1, pageVersion = -1;
        static bool redraw;   // la pagina va rifatta (un clic, una scheda nuova)

        /// Una partita è appena stata registrata, o l'evento è cambiato: alla prossima occhiata si ricalcola.
        internal static void Invalidate() => dirty = true;

        internal static CoachState State
        {
            get
            {
                // si ricalcola se qualcosa è cambiato (partita finita, log letti in sottofondo, evento salvato) o dopo 30 secondi
                if (dirty || storeSeen != CoachStore.Version || Time.realtimeSinceStartup - stateAt > 30)
                {
                    dirty = false;
                    storeSeen = CoachStore.Version;
                    stateAt = Time.realtimeSinceStartup;
                    try { state = CoachStore.State(); }
                    catch (Exception e) { state = null; Plugin.Log.LogWarning("coach: stato non calcolato: " + e.Message); }
                    // la versione sale solo se cambia qualcosa che si vede: se no le pagine si rifarebbero ogni 30 secondi per niente
                    var sign = Signature(state);
                    if (sign != shown) { shown = sign; version++; }
                }
                return state;
            }
        }

        static string shown;

        static string Signature(CoachState st)
        {
            if (st == null) return "-";
            var ev = st.ev;
            return string.Join("|", Coach.Key(st.today), st.total, st.games.Count, st.todayCount, st.todayTarget, st.rate, st.streak, st.bestStreak,
                st.all.wins, st.all.losses, st.warning, st.level, ev.id, ev.name, ev.goal, ev.start, ev.eventDate, ev.readyBy, ev.restDays.Count,
                ev.format, ev.leader, ev.ladder, ev.queue, ev.privateGames, ev.solo);
        }

        internal static bool IsOpen => root != null && root.activeSelf;
        /// Si scrive il nome dell'evento: il sim non deve leggere i tasti.
        internal static bool Typing => IsOpen && nameField != null && nameField.isFocused;

        // ---- a ogni fotogramma (Plugin.Update), ma il lavoro vero una volta al secondo ----

        internal static void Tick()
        {
            // Settings si è appena aperta: il pulsante si mette al suo posto adesso (da spenta Unity non sa le misure)
            if (settingsButton != null && settingsButton.activeInHierarchy != settingsShown)
            {
                settingsShown = settingsButton.activeInHierarchy;
                if (settingsShown) PlaceSettingsButton();
            }
            if (IsOpen)
            {
                // Esc chiude prima il menu a tendina aperto, poi la pagina
                if (Input.GetKeyDown(KeyCode.Escape) && !TMP_DropdownOpen()) Close();
                // dati nuovi (lo stato è cambiato davvero): Today e Stats si rifanno; il Setup no (perderebbe quello che stai scrivendo)
                else
                {
                    var _ = State;
                    if (redraw || (tab != "setup" && pageVersion != version)) Rebuild();
                }
            }
            if (Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + 1f;

            if (host == null)
            {
                host = UnityEngine.Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                built = false;
                if (host == null) return;
            }
            if (!built) TryBuild();
            if (!built) return;

            var st = (menuButton != null && menuButton.transform.parent.gameObject.activeInHierarchy) || (settingsButton != null && settingsButton.activeInHierarchy)
                || (host.go_IPAddress != null && host.go_IPAddress.activeInHierarchy) ? State : state;
            bool active = st != null;

            // il pulsante del menu segue Settings (Batsu accende e spegne i pulsanti del menu uno per uno) e c'è solo con un evento
            if (menuButton != null && host.go_Settings != null)
            {
                bool show = active && host.go_Settings.activeSelf;
                if (menuButton.activeSelf != show) menuButton.SetActive(show);
                if (show) SetText(menuStatus, StatusLine(st));
            }
            if (settingsStatus != null) SetText(settingsStatus, active ? StatusLine(st) : "Plan your games for a tournament");
            UpdatePrivateBox(st);
        }

        static void SetText(TMP_Text t, string text) { if (t != null && t.text != text) t.text = text; }

        // la riga sopra il pulsante: corta (sopra c'è poco posto, in Settings ci sono le caselle di Batsu accanto)
        static string StatusLine(CoachState st)
        {
            var name = Safe(Coach.Short(st.ev.name, 20));
            if (st.beforeStart) return $"{name} · starts {Day(st.start)}";
            if (st.daysToEvent <= 0) return $"{name} · <b>event day</b>";
            var today = st.goalReached ? "<b>EVENT READY</b>" : st.restToday ? "rest day today" : $"<b>{st.todayCount}/{st.todayTarget} today</b>";
            return $"{name} · {Days(st.daysToEvent)} · {today}";
        }

        internal static string Days(int n) => n == 0 ? "today" : n == 1 ? "1 day" : n + " days";

        // il nome dell'evento lo scrive il giocatore: si mostra com'è, senza che diventi formattazione
        internal static string Safe(string text) => "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";

        static bool TMP_DropdownOpen() => root != null && root.GetComponentsInChildren<TMP_Dropdown>(true).Any(d => d.IsExpanded);

        // ---- i pulsanti nel menu e in Settings, il riquadro in Private ----

        static void TryBuild()
        {
            // come MenuButton: si copia dopo che Batsu ha tradotto le scritte del menu
            var settings = host.go_Settings;
            var label = settings != null ? settings.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null || label.text.StartsWith("Button.")) return;
            CoachLook.UseFont(label);
            try
            {
                BuildMenuButton();
                BuildSettingsButton();
                built = true;
                Plugin.Log.LogInfo("coach: pulsanti aggiunti (menu e Settings)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("coach: pulsanti non aggiunti: " + e);
                foreach (var go in new[] { menuButton, settingsButton }) if (go != null) UnityEngine.Object.Destroy(go);
                menuButton = settingsButton = null;
                host = null; // si riprova tra un secondo
            }
        }

        const string CoachLabel = "<size=46>EVENT</size>\n<size=29><cspace=0.2em><color=" + Look.GoldHex + ">COACH</color></cspace></size>";

        static GameObject CoachButton(GameObject template, Transform parent)
        {
            var copy = Look.CloneButton(template, "OPTCGTrainerCoach", CoachLabel, Look.Blue, () => Open(State != null ? "today" : "setup"));
            copy.transform.SetParent(parent, false);
            var text = copy.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.enableAutoSizing = false;
                text.fontSize = 46;
                text.lineSpacing = -22;
                text.alignment = TextAlignmentOptions.Center;
                text.margin = new Vector4(0, 4, 0, 0);
            }
            return copy;
        }

        // la riga piccola sopra un pulsante (come "Linked as Leo" sopra OPTCG TRAINER): dentro la copia, così compare e sparisce con lei
        static TextMeshProUGUI StatusAbove(GameObject button, float width = 1.4f)
        {
            var rect = (RectTransform)button.transform;
            float h = rect.rect.height;
            var t = Look.NewText(rect, button.GetComponentInChildren<TMP_Text>(true), "Status", h * 0.165f, TextAlignmentOptions.Bottom);
            var srt = t.rectTransform;
            srt.anchorMin = new Vector2(0.5f - width / 2, 1); srt.anchorMax = new Vector2(0.5f + width / 2, 1); srt.pivot = new Vector2(0.5f, 0);
            srt.anchoredPosition = new Vector2(0, h * 0.06f);
            srt.sizeDelta = new Vector2(0, h * 0.3f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // se non ci sta, si rimpicciolisce (in Settings a destra ci sono le caselle di Batsu)
            t.enableAutoSizing = true;
            t.fontSizeMin = h * 0.11f;
            t.fontSizeMax = h * 0.165f;
            t.color = new Color(1, 1, 1, 0.85f);
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(60, 25, 0, 200);
            return t;
        }

        static void BuildMenuButton()
        {
            var patreon = host.go_Patreon;
            var settings = host.go_Settings;
            if (patreon == null) return;
            menuButton = CoachButton(settings, settings.transform.parent);
            menuButton.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
            var rt = (RectTransform)menuButton.transform;
            rt.sizeDelta = ((RectTransform)settings.transform).sizeDelta;
            // a specchio di Become a Patron rispetto al centro dello schermo, un poco più giù (sopra ci va la riga dello stato)
            var canvas = (RectTransform)patreon.GetComponentInParent<Canvas>().rootCanvas.transform;
            var center = canvas.TransformPoint(canvas.rect.center);
            var p = patreon.transform.position;
            rt.position = new Vector3(2 * center.x - p.x, p.y, p.z);
            rt.anchoredPosition -= new Vector2(0, rt.rect.height * 14f / 110f);
            menuStatus = StatusAbove(menuButton);
            menuButton.SetActive(false);
        }

        static void BuildSettingsButton()
        {
            var canvas = host.go_SettingsCanvas;
            var back = host.go_SettingsBackButton;
            var resolution = canvas != null ? canvas.GetComponentInChildren<TMP_Dropdown>(true) : null;
            if (canvas == null || back == null || resolution == null) return;
            settingsButton = CoachButton(host.go_Settings, canvas.transform);
            var rt = (RectTransform)settingsButton.transform;
            rt.sizeDelta = ((RectTransform)host.go_Settings.transform).sizeDelta;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            settingsStatus = StatusAbove(settingsButton, 1.04f); // larga quanto il pulsante: non arriva alle caselle di Batsu
            settingsShown = false;
        }

        // nella colonna vuota a sinistra, centrato su Resolution e 192 px (del disegno) sopra il suo menu a tendina
        static void PlaceSettingsButton()
        {
            var canvas = host != null ? host.go_SettingsCanvas : null;
            var resolution = canvas != null ? canvas.GetComponentInChildren<TMP_Dropdown>(true) : null;
            if (resolution == null || host.go_SettingsBackButton == null) return;
            Canvas.ForceUpdateCanvases();
            var res = (RectTransform)resolution.transform;
            var resLocal = (Vector2)canvas.transform.InverseTransformPoint(res.TransformPoint(res.rect.center));
            float k = ((RectTransform)host.go_SettingsBackButton.transform).rect.height / 110f; // quanto vale un pixel del disegno qui
            ((RectTransform)settingsButton.transform).anchoredPosition = resLocal + new Vector2(0, 192 * k);
        }

        // ---- il riquadro nella schermata Private (disegno coach-e-private) ----

        static void UpdatePrivateBox(CoachState st)
        {
            bool show = st != null && Plugin.HideMultiplayerExtras.Value && host.go_IPAddress != null && host.go_IPAddress.activeInHierarchy
                && host.go_OPBounty != null && host.go_OPBounty.activeInHierarchy && host.go_DeckSelector != null && !IsOpen;
            if (!show) { if (privateBox != null) privateBox.SetActive(false); return; }
            if (privateBox == null)
            {
                try { BuildPrivateBox(); }
                catch (Exception e) { Plugin.Log.LogWarning("coach: riquadro in Private non fatto: " + e.Message); return; }
            }
            privateBox.SetActive(true);
            if (privateDrawn != version) { privateDrawn = version; DrawPrivate((RectTransform)privateBox.transform, st); }
        }

        static void BuildPrivateBox()
        {
            // misure e posto dal selettore del mazzo (nel disegno: 263, 194, 292×52; il riquadro: 196, 286, 428×246)
            var deck = (RectTransform)host.go_DeckSelector.transform;
            var canvas = (RectTransform)deck.GetComponentInParent<Canvas>().rootCanvas.transform;
            var c = new Vector3[4];
            deck.GetWorldCorners(c);
            Vector2 topLeft = canvas.InverseTransformPoint(c[1]), topRight = canvas.InverseTransformPoint(c[2]);
            float k = (topRight.x - topLeft.x) / 292f;
            privateBox = new GameObject("OPTCGTrainerCoachToday", typeof(RectTransform));
            var rt = (RectTransform)privateBox.transform;
            rt.SetParent(canvas, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(428, 246);
            rt.localScale = new Vector3(k, k, 1);
            rt.anchoredPosition = topLeft + new Vector2(-67 * k, -92 * k);
            privateDrawn = -1;
        }

        static void DrawPrivate(RectTransform box, CoachState st)
        {
            foreach (Transform child in box) UnityEngine.Object.Destroy(child.gameObject);
            const float X = -196, Y = -286; // le posizioni del disegno, spostate nel riquadro
            var bg = CoachLook.Box(box, 0, 0, 428, 246, new Color(CoachLook.Panel.r, CoachLook.Panel.g, CoachLook.Panel.b, 0.94f), 6, true);
            CoachLook.Frame(box, 0, 0, 428, 246, CoachLook.PanelLine, 2);
            CoachLook.Hot(box, 0, 0, 428, 246, () => Open("today"));
            CoachLook.Caps(box, 220 + X, 304 + Y, 300, "TODAY · " + Safe(Coach.Short(st.ev.name, 18).ToUpperInvariant()), 14, TextAlignmentOptions.TopLeft, false);
            CoachLook.Write(box, 420 + X, 303 + Y, 184, 20, Days(st.daysToEvent) + (st.daysToEvent > 0 ? " left" : ""), 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            CoachLook.Write(box, 218 + X, 318 + Y, 200, 70,
                st.todayTarget > 0 ? $"<size=64>{st.todayCount}</size><size=38><color=#9aa6c2> / {st.todayTarget}</color></size><size=22><color=#c9d1e3>  games</color></size>"
                    : $"<size=64>{st.todayCount}</size><size=22><color=#c9d1e3>  {(st.todayCount == 1 ? "game" : "games")}</color></size>",
                22, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            var (top, under) = TodayMessage(st, true);
            CoachLook.Write(box, 400 + X, 338 + Y, 204, 28, top, 21, CoachLook.Gold, true, TextAlignmentOptions.TopRight, true);
            CoachLook.Write(box, 400 + X, 366 + Y, 204, 20, under, 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            int n = Mathf.Min(st.todayTarget, 6);
            for (int i = 0; i < n; i++)
            {
                bool on = i < st.todayCount;
                float x = 220 + X + i * 66;
                CoachLook.Box(box, x, 392 + Y, 58, 58, on ? CoachLook.Gold : CoachLook.Plan, 6);
                if (on) CoachLook.Picture(box, CoachLook.Check, x + 12, 392 + Y + 12, 34, 34);
            }
            // più di 6: le altre come "+7", sotto l'ultima casella (dentro il riquadro)
            if (st.todayTarget > 6) CoachLook.Write(box, 220 + X + 5 * 66, 392 + Y + 60, 58, 18, "+" + (st.todayTarget - 6), 13, CoachLook.Label, true, TextAlignmentOptions.TopRight);
            // gli ultimi 7 giorni, piccoli
            CoachLook.Picture(box, CoachLook.Flame, 218 + X, 470 + Y, 24, 28);
            var last = st.days.Where(d => !d.future).Skip(Math.Max(0, st.days.Count(d => !d.future) - 7)).ToList();
            for (int i = 0; i < last.Count; i++) SmallDay(box, 256 + X + i * 28, 474 + Y, 22, last[i]);
            CoachLook.Write(box, 256 + X + last.Count * 28 + 6, 471 + Y, 200, 26, st.streak == 1 ? "1-day streak" : st.streak + "-day streak", 19, Color.white, true, TextAlignmentOptions.TopLeft, true);
            CoachLook.Write(box, 220 + X, 504 + Y, 390, 20, $"Pass level {st.level} / {Coach.Levels} · open Event Coach ›", 14, CoachLook.Label);
            bg.transform.SetAsFirstSibling();
        }

        // un giorno in piccolo: oro = numero fatto, a righe = riposo, oggi con il bordo e mezzo pieno
        static void SmallDay(Transform parent, float x, float y, float s, CoachDay d)
        {
            if (d.rest)
            {
                CoachLook.Box(parent, x, y, s, s, CoachLook.RestA, 3);
                CoachLook.Stripes(parent, x, y, s, s, CoachLook.RestB);
            }
            else if (d.Met) CoachLook.Box(parent, x, y, s, s, CoachLook.Gold, 3);
            else
            {
                CoachLook.Box(parent, x, y, s, s, CoachLook.Plan, 3);
                if (d.isToday && d.target > 0 && d.record.games > 0)
                {
                    float f = Mathf.Clamp01(d.record.games / (float)d.target);
                    CoachLook.Box(parent, x, y + s * (1 - f), s, s * f, CoachLook.PartGold, 3);
                }
            }
            if (d.isToday) CoachLook.Frame(parent, x - 2, y - 2, s + 4, s + 4, Color.white, 2);
        }

        /// La riga a fine partita, sotto il riquadro della mod: quanto manca oggi, o perché questa partita non conta.
        /// null senza un evento (o se la partita non è ancora riassunta).
        internal static string EndLine(MatchFile m)
        {
            if (m == null || m.game == null) return null;
            var st = State;
            if (st == null) return null;
            var g = st.games.FirstOrDefault(x => x.ids.Contains(m.id));
            if (g == null) return null;
            if (g.why != null) return $"Event Coach: {g.why}, not counted";
            // niente numero di oggi: il giorno dell'evento, l'obiettivo già raggiunto, un riposo
            if (st.daysToEvent <= 0) return $"Event Coach: {st.total}/{st.goal} games · event day";
            if (st.goalReached) return $"Event Coach: <color={Look.GoldHex}>EVENT READY</color> · {Coach.N(st.total, "game")}";
            if (st.restToday || st.todayTarget == 0) return $"Event Coach: {st.todayCount} today · rest day";
            return $"Event Coach: <b>{st.todayCount}/{st.todayTarget} today</b>" + (st.todayLeft == 0 ? $" · <color={Look.GoldHex}>done</color>" : $" · {st.todayLeft} to go");
        }

        /// La frase grande a destra di "3 / 6 games": cosa manca oggi e perché.
        static (string top, string under) TodayMessage(CoachState st, bool shortForm)
        {
            if (st.beforeStart) return ("Starts " + Day(st.start), "");
            if (st.over || st.daysToEvent == 0) return ("Event day", "good luck!");
            if (st.goalReached) return ("EVENT READY", st.ev.goal + " games done");
            if (st.restToday) return ("Rest day", "no games needed today");
            if (st.todayLeft == 0) return ("Done for today", st.streak > 1 ? $"{st.streak}-day streak" : "streak started");
            var more = st.todayLeft == 1 ? "1 more today" : st.todayLeft + " more today";
            if (shortForm) return (more, st.streak > 0 ? "to keep the streak" : "to start a streak");
            return (more, st.streak > 0 ? $"to keep your {st.streak}-day streak" : "to start a streak");
        }

        // ---- la pagina ----

        /// Apre la pagina del coach su una scheda: today, stats, setup.
        internal static void Open(string which)
        {
            try
            {
                dirty = true;
                if (State == null && which != "setup") which = "setup";
                if (root == null) BuildRoot();
                if (root == null) return;
                tab = which;
                if (which == "setup") ResetSetup();
                root.SetActive(true);
                Rebuild();
            }
            catch (Exception e) { Plugin.Log.LogError("coach: pagina non aperta: " + e); Close(); }
        }

        internal static void Close()
        {
            if (nameField != null) nameField.DeactivateInputField();
            if (root != null) root.SetActive(false);
            privateDrawn = -1;
        }

        static void BuildRoot()
        {
            if (host == null) return;
            CoachLook.UseFont(host.go_Settings.GetComponentInChildren<TMP_Text>(true));
            root = new GameObject("OPTCGTrainerCoachPage", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // sopra al menu di Batsu e al pannello di OPBounty (10)
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CoachLook.W, CoachLook.H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();
            // lo sfondo di legno: la stessa immagine di Batsu, su tutto lo schermo; prende i clic, così sotto non arriva niente
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)bg.transform;
            brt.SetParent(root.transform, false);
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var img = bg.GetComponent<Image>();
            var wood = host.go_Settings.transform.parent.Find("BG")?.GetComponent<Image>();
            if (wood != null) { img.sprite = wood.sprite; img.color = wood.color; img.type = wood.type; img.preserveAspect = wood.preserveAspect; }
            else img.color = new Color(0.55f, 0.3f, 0.12f);
            page = (RectTransform)new GameObject("Page", typeof(RectTransform)).transform;
            page.SetParent(root.transform, false);
            page.anchorMin = page.anchorMax = page.pivot = new Vector2(0.5f, 0.5f);
            page.sizeDelta = new Vector2(CoachLook.W, CoachLook.H);
            root.SetActive(false);
        }

        static void Rebuild()
        {
            if (page == null) return;
            redraw = false;
            var st = State;
            pageVersion = version;
            // la pagina cambia scheda da sola (l'evento è finito in archivio): il Setup riparte da un evento nuovo, non dalla bozza vecchia
            if (st == null && tab != "setup") { tab = "setup"; ResetSetup(); }
            else if (tab == "setup" && editing && CoachStore.Data.active == null) ResetSetup();
            foreach (Transform child in page) UnityEngine.Object.Destroy(child.gameObject);
            try
            {
                if (tab == "setup") BuildSetup(page, st);
                else
                {
                    Header(page, st, tab == "stats"
                        ? $"since {Day(st.start)} · {Kinds(st.ev)} · {st.ev.format ?? "any format"} · turn {Coach.MinTurns}+"
                        : string.Join(" · ", new[] { Day(st.eventDay), st.ev.format ?? "any format", st.ev.leader != null ? LeaderName(st.ev.leader, st.leaderName) : null }.Where(x => x != null)));
                    if (tab == "stats") BuildStats(page, st); else BuildToday(page, st);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("coach: schermata " + tab + " non fatta: " + e); }
            CoachLook.Button(page, host.go_SettingsBackButton, 813, 807, 294, 110, "Back", false, Close, 64);
        }

        // il riquadro blu notte, il titolo e le schede
        static void Header(RectTransform p, CoachState st, string subtitle)
        {
            CoachLook.Box(p, 140, 76, 1640, 706, CoachLook.Panel, 0, true);
            CoachLook.Frame(p, 140, 76, 1640, 706, CoachLook.PanelLine, 2);
            var title = CoachLook.Write(p, 184, 94, 1066, 50, $"<size=34>{Safe(Coach.Short(st.ev.name, 26).ToUpperInvariant())}</size>  <size=19><color=#c9d1e3>{subtitle}</color></size>",
                19, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            title.overflowMode = TextOverflowModes.Ellipsis; // non deve finire sotto le schede
            Tab(p, 1266, "TODAY", "today");
            Tab(p, 1426, "STATS", "stats");
            Tab(p, 1586, "SETUP", "setup");
            CoachLook.Box(p, 184, 162, 1552, 1, CoachLook.CardLine);
        }

        static void Tab(RectTransform p, float x, string label, string which)
        {
            CoachLook.Button(p, host.go_Settings, x, 98, 150, 52, label, tab == which, () => { tab = which; if (which == "setup") ResetSetup(); redraw = true; }, 19);
        }

        internal static void Card(RectTransform p, float x, float y, float w, float h)
        {
            CoachLook.Box(p, x, y, w, h, CoachLook.Card, 4);
            CoachLook.Frame(p, x, y, w, h, CoachLook.CardLine, 1);
        }

        // ---- parole ----

        internal static string Day(DateTime d) => d.ToString("ddd d MMM", CultureInfo.InvariantCulture);
        internal static string DayYear(DateTime d) => d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

        static string Kinds(CoachEvent ev) => string.Join(", ", new[] { ev.ladder ? "ladder" : null, ev.queue ? "queue" : null, ev.privateGames ? "private" : null, ev.solo ? "Solo v Self" : null }.Where(x => x != null));

        // "Purple Doflamingo": i colori e l'ultima parola del nome, dal database delle carte del sim (se non c'è: il nome del log)
        static readonly Dictionary<string, string> leaderNames = new Dictionary<string, string>();

        internal static string LeaderName(string id, string fromLog = null)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (leaderNames.TryGetValue(id, out var known)) return known;
            string name = fromLog, colors = null;
            bool fromDb = false;
            try
            {
                var db = CardDatabaseScript.Instance;
                if (db != null && db.dict_Cards != null && db.dict_Cards.TryGetValue(id, out var card) && card != null && card.cardDefinition != null)
                {
                    name = card.cardDefinition.characterName ?? name;
                    if (card.cardDefinition.cardColors != null && card.cardDefinition.cardColors.Count > 0)
                        colors = string.Join("-", card.cardDefinition.cardColors.Select(c => c.ToString()));
                    fromDb = true;
                }
            }
            catch (Exception) { /* il database delle carte non c'è ancora: il nome del log */ }
            var last = (name ?? "").Split(new[] { ' ', '.', '"' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? id;
            var result = colors != null ? colors + " " + last : last;
            if (fromDb) leaderNames[id] = result;
            return result;
        }
    }
}
