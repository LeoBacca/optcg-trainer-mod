// Event Coach: la scheda Setup (disegno coach-b2-setup, scelto da Leo dopo la prima versione: "non si capiva cosa è
// importante e in che ordine"). A sinistra i passi in ordine: 1 l'evento, 2 il piano, 3 il facoltativo (più piccolo e grigio).
// A destra il risultato: il piano sul calendario, quante partite al giorno, un pulsante solo.
// Si lavora su una copia dell'evento (draft): niente cambia finché non premi START COACHING (o SAVE CHANGES, se l'evento
// c'è già). Il calendario serve anche per le date: si accende un campo (Date, Ready by, Start from, Rest days), si cliccano
// i giorni, Done. I riposi si possono mettere anche sui giorni passati (scelta di Leo).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static partial class CoachUi
    {
        static CoachEvent draft;
        static bool editing, readyTouched, stopArmed;
        static string field = "date";      // start · date · ready · rest: cosa fanno i clic sul calendario (null: niente)
        static int monthShift;
        static TMP_InputField nameField;
        static DateTime today;
        // calcolati una volta quando il Setup si apre (non a ogni clic: costerebbero 2–4 fotogrammi)
        static List<CoachGame> setupGames;
        static List<string> setupFormats, setupLeaders;

        static readonly Color Picking = new Color(0.42f, 0.61f, 1f), PickText = new Color(0.81f, 0.88f, 1f), OptCard = new Color(0.145f, 0.192f, 0.31f),
            OptRing = new Color(0.365f, 0.42f, 0.553f), SmallLabel = new Color(0.725f, 0.76f, 0.84f), Ink = new Color(0.07f, 0.07f, 0.08f);

        /// Un evento nuovo (o la copia di quello attivo), da cambiare senza toccare niente finché non si salva.
        static void ResetSetup()
        {
            today = Coach.DayOf(CoachStore.Now);
            setupGames = CoachStore.Games(); // fa partire anche la lettura in sottofondo dei log, se serve
            setupFormats = Formats();
            setupLeaders = Leaders(setupGames);
            var active = CoachStore.Data.active;
            editing = active != null;
            stopArmed = false;
            monthShift = 0;
            if (editing)
            {
                draft = JsonConvert.DeserializeObject<CoachEvent>(JsonConvert.SerializeObject(active));
                field = null;
                readyTouched = true;
                return;
            }
            var eventDay = today.AddDays(14);
            draft = new CoachEvent
            {
                name = "", start = Coach.Key(today), eventDate = Coach.Key(eventDay), readyBy = Coach.Key(eventDay.AddDays(-2)), goal = 100,
                format = setupFormats.Contains("Standard") ? "Standard" : null,
            };
            field = "date"; // la prima cosa da scegliere sul calendario: il giorno dell'evento
            readyTouched = false;
        }

        static void BuildSetup(RectTransform p, CoachState st)
        {
            if (draft == null || setupGames == null) ResetSetup();
            today = Coach.DayOf(CoachStore.Now);
            CoachLook.Box(p, 140, 76, 1640, 706, CoachLook.Panel, 0, true);
            CoachLook.Frame(p, 140, 76, 1640, 706, CoachLook.PanelLine, 2);
            CoachLook.Write(p, 184, 94, 700, 48, $"EVENT COACH <color={Look.GoldHex}>· SETUP</color>", 34, Color.white, true, TextAlignmentOptions.TopLeft, true);
            if (editing)
            {
                CoachLook.Write(p, 600, 110, 640, 24, "Changes apply from today: past days keep their numbers.", 17, CoachLook.Label);
                Tab(p, 1266, "TODAY", "today"); Tab(p, 1426, "STATS", "stats"); Tab(p, 1586, "SETUP", "setup");
            }
            else CoachLook.Write(p, 900, 108, 836, 24, "Fill in 1 and 2, check your plan on the right, then Start.", 17, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            CoachLook.Box(p, 184, 158, 1552, 1, CoachLook.CardLine);

            DateTime eventDay = Coach.Day(draft.eventDate) ?? today.AddDays(14), ready = Coach.Day(draft.readyBy) ?? eventDay.AddDays(-2), start = Coach.Day(draft.start) ?? today;
            var rest = new HashSet<DateTime>(draft.restDays.Select(Coach.Day).Where(d => d != null).Select(d => d.Value));

            // 1 · l'evento
            Card(p, 184, 174, 832, 150);
            StepTitle(p, 184, "1", "The event", false);
            FormLabel(p, 222, "Name");
            NameField(p, 420, 222, 572);
            FormLabel(p, 272, "Date");
            DateField(p, 272, "date", DayYear(eventDay));
            CoachLook.Write(p, 684, 272, 78, 40, "Format", 19, CoachLook.FormText, false, TextAlignmentOptions.MidlineLeft);
            var formats = new List<string> { "Any format" };
            formats.AddRange(setupFormats);
            Dropdown(p, 762, 275, 230, formats, Math.Max(0, formats.IndexOf(draft.format ?? "Any format")), i => draft.format = i == 0 ? null : formats[i], 19);

            // 2 · il piano
            Card(p, 184, 338, 832, 250);
            StepTitle(p, 348, "2", "Your plan", false);
            FormLabel(p, 386, "Games goal");
            CoachLook.Button(p, host.go_Settings, 420, 386, 44, 40, "−", false, () => Goal(-Step()), 26);
            Field(p, 472, 386, 100, 40, draft.goal.ToString(CultureInfo.InvariantCulture), false, null, TextAlignmentOptions.Center, 22, true);
            CoachLook.Button(p, host.go_Settings, 580, 386, 44, 40, "+", false, () => Goal(Step()), 26);
            Hint(p, 644, 386, "games to play before Ready by", false);
            FormLabel(p, 436, "Ready by");
            DateField(p, 436, "ready", DayYear(ready));
            int before = (eventDay - ready).Days;
            Hint(p, 670, 436, before == 0 ? "the event day" : before == 1 ? "the day before the event" : before + " days before the event", field == "ready");
            FormLabel(p, 486, "Start from");
            DateField(p, 486, "start", DayYear(start));
            int ago = (today - start).Days;
            Hint(p, 670, 486, ago == 0 ? "today · games from this day count" : ago > 0 ? $"{(ago == 1 ? "yesterday" : ago + " days ago")} · games with the mod since then count"
                : "the plan starts that day", field == "start");
            FormLabel(p, 536, "Rest days");
            DateField(p, 536, "rest", RestSummary(rest));
            Hint(p, 670, 536, "pick them on the calendar →", field == "rest");

            // 3 · facoltativo: più piccolo e grigio
            CoachLook.Box(p, 184, 602, 832, 164, OptCard, 4);
            CoachLook.Frame(p, 184, 602, 832, 164, CoachLook.CardLine, 1);
            StepTitle(p, 612, "3", "Optional", true, "the defaults are fine");
            // fermare l'evento, o gli eventi passati: piccoli, in alto a destra di questo riquadro (non disturbano)
            if (editing) CoachLook.Button(p, host.go_Settings, 846, 612, 150, 30, stopArmed ? "Click again to stop" : "Stop coaching", false, StopClick, 15);
            else if (CoachStore.Data.archive.Count > 0)
            {
                var past = CoachStore.Data.archive.AsEnumerable().Reverse().Take(2).Select(a => $"{Safe(Coach.Short(a.ev.name, 14))} {a.games}/{a.ev.goal}");
                var line = CoachLook.Write(p, 580, 616, 416, 22, "Past events: " + string.Join(" · ", past), 14, CoachLook.Dim, false, TextAlignmentOptions.TopRight);
                line.overflowMode = TextOverflowModes.Ellipsis; // non deve coprire "the defaults are fine"
            }
            FormLabel(p, 652, "Your leader", true);
            var leaders = setupLeaders;
            var leaderNames = new List<string> { "No leader" };
            leaderNames.AddRange(leaders.Select(id => LeaderName(id)));
            Dropdown(p, 420, 655, 230, leaderNames, Math.Max(0, leaders.IndexOf(draft.leader) + 1), i => draft.leader = i == 0 ? null : leaders[i - 1], 17);
            Hint(p, 670, 652, "shows your leader's games separately", false);
            FormLabel(p, 702, "Games that count", true);
            Check(p, 420, 702, "Ladder", draft.ladder, v => draft.ladder = v);
            Check(p, 530, 702, "Queue", draft.queue, v => draft.queue = v);
            Check(p, 637, 702, "Private", draft.privateGames, v => draft.privateGames = v);
            Check(p, 748, 702, "Solo v Self", draft.solo, v => draft.solo = v);
            CoachLook.Write(p, 420, 740, 560, 20, $"Played with the mod, in the event format, at least turn {Coach.MinTurns}.", 14, CoachLook.Label);

            // a destra: il piano
            Card(p, 1030, 174, 706, 592);
            CoachLook.Caps(p, 1052, 188, 300, "Your plan");
            Calendar(p, eventDay, ready, rest);
            CoachLook.Box(p, 1052, 584, 662, 1, CoachLook.CardLine);

            // il conto: come sarebbe oggi con queste regole (con "Start from" nel passato, contano le partite già fatte)
            var problem = Coach.Problem(draft, today);
            if (problem == "Give the event a name.") problem = null; // il nome si chiede solo premendo Start
            var (playDays, restDays, _) = Coach.Preview(draft);
            var games = setupGames;
            CoachState preview = null;
            if (problem == null)
            {
                // come sarà salvando: i numeri fissati restano solo per i giorni passati (in modifica), oggi si ricalcola
                var copy = Coach.KeepPastTargets(draft, today);
                if (!editing) copy.targets.Clear();
                preview = Coach.Compute(copy, games, CoachStore.Now);
            }
            int perDay = preview?.rate ?? 0;
            var big = CoachLook.Write(p, 1052, 588, 420, 76, $"<size=64><color={Look.GoldHex}>{(preview != null ? perDay.ToString(CultureInfo.InvariantCulture) : "–")}</color></size><size=30>  games a day</size>",
                30, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            var avg = Coach.AverageMinutes(draft, games);
            if (preview != null && avg != null)
            {
                big.ForceMeshUpdate();
                CoachLook.Write(p, 1052 + big.preferredWidth + 18, 588, 300, 72, $"≈ {Duration(avg.Value * perDay)} a day", 17, CoachLook.Label, false, TextAlignmentOptions.BottomLeft);
            }
            string done = preview != null && preview.total > 0 ? $" · {preview.total} already played" : "";
            CoachLook.Write(p, 1052, 668, 662, 24, problem ?? $"{Coach.N(draft.goal, "game")} in {Coach.N(playDays, "play day")} · {Day(start)} → {Day(ready)} · {Coach.N(restDays, "rest day")}{done}",
                16, problem != null ? CoachLook.Warn : CoachLook.Soft);
            CoachLook.Button(p, host.go_Settings, 1052, 698, 662, 58, editing ? "SAVE CHANGES" : "START COACHING", true, StartClick, 26);
        }

        // il titolo di un passo: il numero nel cerchio (oro; il facoltativo solo il contorno) e il nome in maiuscolo
        static void StepTitle(RectTransform p, float y, string num, string title, bool optional, string sub = null)
        {
            CoachLook.Circle(p, 221, y + 17, 34, optional ? OptRing : CoachLook.Gold, optional);
            CoachLook.Write(p, 204, y, 34, 34, num, 19, optional ? CoachLook.Label : CoachLook.Dark, true, TextAlignmentOptions.Center);
            var t = CoachLook.Write(p, 250, y + 2, 400, 30, title.ToUpperInvariant(), 20, optional ? CoachLook.Label : Color.white, true, TextAlignmentOptions.MidlineLeft);
            t.characterSpacing = 12 + CoachLook.BoldTighten;
            if (sub == null) return;
            t.ForceMeshUpdate();
            CoachLook.Write(p, 250 + t.preferredWidth + 16, y + 2, 300, 30, sub, 15, CoachLook.Label, false, TextAlignmentOptions.MidlineLeft);
        }

        static string Duration(double minutes) => minutes < 60 ? Math.Round(minutes) + " min" : (minutes / 60).ToString(minutes < 600 ? "0.#" : "0", CultureInfo.InvariantCulture) + " h";

        static int Step() => draft.goal >= 200 ? 10 : 5;

        static void Goal(int by)
        {
            draft.goal = Mathf.Clamp((draft.goal + by) / Math.Abs(by) * Math.Abs(by), 5, 2000);
            redraw = true;
        }

        static string RestSummary(HashSet<DateTime> rest)
        {
            if (rest.Count == 0) return "none";
            var days = rest.Select(d => d.DayOfWeek).Distinct().ToList();
            return days.Count == 1 ? $"{rest.Count} · {days[0]}s" : rest.Count == 1 ? "1 day" : rest.Count + " days";
        }

        static void FormLabel(RectTransform p, float y, string text, bool small = false) =>
            CoachLook.Write(p, 252, y, 170, 40, text, small ? 17 : 19, small ? SmallLabel : CoachLook.FormText, false, TextAlignmentOptions.MidlineLeft);

        static void Hint(RectTransform p, float x, float y, string text, bool now) =>
            CoachLook.Write(p, x, y, 340, 40, text, 15, now ? new Color(0.56f, 0.71f, 1f) : CoachLook.Label, false, TextAlignmentOptions.MidlineLeft);

        // un campo color carta (come quello per entrare in una stanza di Batsu); quello attivo ha il bordo blu
        static void Field(RectTransform p, float x, float y, float w, float h, string text, bool active, Action click,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float size = 19, bool bold = false)
        {
            if (active) CoachLook.Box(p, x - 3, y - 3, w + 6, h + 6, CoachLook.ReadyLine, 4);
            CoachLook.Box(p, x, y, w, h, CoachLook.Beige, 3);
            CoachLook.Frame(p, x, y, w, h, active ? CoachLook.ReadyLine : new Color(0.71f, 0.65f, 0.49f), active ? 2 : 1);
            CoachLook.Write(p, x + 16, y, w - 32, h, text, size, Ink, bold, align);
            if (click != null) CoachLook.Hot(p, x, y, w, h, click);
        }

        static void DateField(RectTransform p, float y, string which, string text) =>
            Field(p, 420, y, 230, 40, text, field == which, () => { field = which; redraw = true; });

        // il nome: una copia del campo del nome del giocatore di Batsu (Settings), sopra un fondo color carta
        static void NameField(RectTransform p, float x, float y, float w)
        {
            nameField = null;
            var template = host.go_PlayerName;
            if (template == null || template.GetComponent<TMP_InputField>() == null)
            {
                Field(p, x, y, w, 40, Safe(draft.name), false, null);
                return;
            }
            Field(p, x, y, w, 40, "", false, null);
            var copy = Look.SafeCopy(template, p);
            copy.name = "EventName";
            CoachLook.Put((RectTransform)copy.transform, x + 2, y, w - 4, 40);
            var input = copy.GetComponent<TMP_InputField>();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 40;
            input.text = draft.name ?? "";
            input.onValueChanged.AddListener(v => draft.name = v);
            if (copy.GetComponent<Image>() is Image bg) bg.color = new Color(1, 1, 1, 0);
            if (input.textComponent != null)
            {
                input.textComponent.fontSize = 19;
                input.textComponent.color = Ink;
                input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (input.placeholder is TMP_Text ph)
            {
                ph.text = "e.g. Regionals Milan";
                ph.fontSize = 19;
                ph.alignment = TextAlignmentOptions.MidlineLeft;
            }
            copy.SetActive(true);
            nameField = input;
        }

        // un menu a tendina: copia di quello della risoluzione di Batsu (Settings)
        static void Dropdown(RectTransform p, float x, float y, float w, List<string> options, int value, Action<int> changed, float size)
        {
            var template = host.go_SettingsCanvas != null ? host.go_SettingsCanvas.GetComponentInChildren<TMP_Dropdown>(true) : null;
            if (template == null) { Field(p, x, y, w, 34, options[value], false, null, TextAlignmentOptions.MidlineLeft, size); return; }
            var copy = Look.SafeCopy(template.gameObject, p);
            copy.name = "Dropdown";
            CoachLook.Put((RectTransform)copy.transform, x, y, w, 34);
            var dd = copy.GetComponent<TMP_Dropdown>();
            dd.onValueChanged = new TMP_Dropdown.DropdownEvent();
            dd.ClearOptions();
            dd.AddOptions(options);
            dd.SetValueWithoutNotify(Mathf.Clamp(value, 0, options.Count - 1));
            dd.RefreshShownValue();
            if (dd.captionText != null) { dd.captionText.fontSize = size; dd.captionText.enableAutoSizing = false; }
            dd.onValueChanged.AddListener(i => { changed(i); redraw = true; });
            copy.SetActive(true);
        }

        // una casella di spunta come quelle di Batsu: quadrato bianco, spunta grigia sottile
        static void Check(RectTransform p, float x, float y, string label, bool on, Action<bool> set)
        {
            CoachLook.Box(p, x, y + 8, 24, 24, Color.white, 4);
            CoachLook.Frame(p, x, y + 8, 24, 24, new Color(0.74f, 0.74f, 0.74f), 1);
            if (on) CoachLook.Picture(p, CoachLook.Tick, x, y + 8, 24, 24);
            var t = CoachLook.Write(p, x + 32, y, 140, 40, label, 17, CoachLook.Soft, false, TextAlignmentOptions.MidlineLeft);
            t.ForceMeshUpdate();
            CoachLook.Hot(p, x, y + 4, 32 + t.preferredWidth + 6, 32, () => { set(!on); redraw = true; });
        }

        // ---- il calendario: due mesi (il primo è quello del primo giorno, o di oggi), la striscia di cosa si sta scegliendo ----

        static readonly Dictionary<string, (string name, string verb)> Picks = new Dictionary<string, (string, string)>
        {
            ["date"] = ("EVENT DATE", "click the day"), ["ready"] = ("READY BY", "click the day"),
            ["start"] = ("START FROM", "click the day"), ["rest"] = ("REST DAYS", "click the days"),
        };

        static void Calendar(RectTransform p, DateTime eventDay, DateTime ready, HashSet<DateTime> rest)
        {
            DateTime from = Coach.Day(draft.start) is DateTime s0 && s0 < today ? s0 : today;
            var first = new DateTime(from.Year, from.Month, 1).AddMonths(monthShift);
            var second = first.AddMonths(1);
            string M(DateTime d, string f) => d.ToString(f, CultureInfo.InvariantCulture); // in inglese, come il resto del sim
            var title = first.Year == second.Year ? $"{M(first, "MMMM")} – {M(second, "MMMM yyyy")}" : $"{M(first, "MMMM yyyy")} – {M(second, "MMMM yyyy")}";
            CoachLook.Write(p, 1250, 186, 412, 22, title, 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            CoachLook.Picture(p, CoachLook.Left, 1664, 181, 30, 30, CoachLook.Soft);
            CoachLook.Hot(p, 1664, 179, 28, 34, () => { monthShift--; redraw = true; });
            CoachLook.Picture(p, CoachLook.Right, 1690, 181, 30, 30, CoachLook.Soft);
            CoachLook.Hot(p, 1692, 179, 28, 34, () => { monthShift++; redraw = true; });

            // la striscia: cosa fanno i clic sul calendario (Done la spegne)
            if (field != null && Picks.TryGetValue(field, out var pick))
            {
                CoachLook.Box(p, 1036, 222, 684, 40, new Color(Picking.r, Picking.g, Picking.b, 0.16f), 4);
                CoachLook.Frame(p, 1036, 222, 684, 40, new Color(Picking.r, Picking.g, Picking.b, 0.55f), 1);
                CoachLook.Write(p, 1052, 222, 560, 40, $"Now picking <b><color=#ffffff><cspace=0.06em>{pick.name}</cspace></color></b>: {pick.verb}", 16, PickText, false, TextAlignmentOptions.MidlineLeft);
                CoachLook.Button(p, host.go_Settings, 1632, 226, 80, 32, "Done", false, () => { field = null; redraw = true; }, 16);
            }
            else
                CoachLook.Write(p, 1052, 222, 668, 40, "To change a date, click it on the left, then pick it here.", 15, CoachLook.Label, false, TextAlignmentOptions.MidlineLeft);

            DateTime start = Coach.Day(draft.start) ?? today;
            Month(p, 1048, first, start, eventDay, ready, rest);
            Month(p, 1400, second, start, eventDay, ready, rest);

            // la legenda
            void Legend(float x, string text, Action<float> swatch) { swatch(x); CoachLook.Write(p, x + 27, 546, 90, 22, text, 14, CoachLook.Soft); }
            Legend(1153, "Today", x => CoachLook.Frame(p, x, 550, 20, 16, Color.white, 2));
            Legend(1238, "Play day", x => CoachLook.Box(p, x, 550, 20, 16, CoachLook.Plan, 3));
            Legend(1336, "Rest day", x => { CoachLook.Box(p, x, 550, 20, 16, CoachLook.RestA, 3); CoachLook.Stripes(p, x, 550, 20, 16, CoachLook.RestB); });
            Legend(1437, "Ready by", x => CoachLook.Box(p, x, 550, 20, 16, CoachLook.Ready, 3));
            Legend(1541, "Event", x => CoachLook.Box(p, x, 550, 20, 16, CoachLook.Gold, 3));
        }

        static void Month(RectTransform p, float x0, DateTime month, DateTime start, DateTime eventDay, DateTime ready, HashSet<DateTime> rest)
        {
            CoachLook.Write(p, x0, 286, 304, 26, month.ToString("MMMM", CultureInfo.InvariantCulture), 17, Color.white, true, TextAlignmentOptions.Top);
            string[] wd = { "MO", "TU", "WE", "TH", "FR", "SA", "SU" };
            for (int c = 0; c < 7; c++) CoachLook.Write(p, x0 + c * 44, 312, 40, 20, wd[c], 12, CoachLook.Dim, true, TextAlignmentOptions.Top);
            int offset = ((int)month.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(month.Year, month.Month);
            for (int i = 0; i < days; i++)
            {
                var d = month.AddDays(i);
                int cell = offset + i;
                DayCell(p, x0 + (cell % 7) * 44, 332 + (cell / 7) * 36, d, start, eventDay, ready, rest);
            }
        }

        static void DayCell(RectTransform p, float x, float y, DateTime d, DateTime start, DateTime eventDay, DateTime ready, HashSet<DateTime> rest)
        {
            const float w = 40, h = 32;
            bool inPlan = d >= start && d <= ready, spare = d > ready && d < eventDay;
            Color text = CoachLook.Text;
            bool bold = false;
            if (d == eventDay)
            {
                CoachLook.Box(p, x, y, w, h, CoachLook.Gold, 3);
                CoachLook.Picture(p, CoachLook.Star, x + w - 12, y + 2, 10, 10, CoachLook.Dark);
                text = CoachLook.Dark; bold = true;
            }
            else if (rest.Contains(d) && (inPlan || spare))
            {
                CoachLook.Box(p, x, y, w, h, CoachLook.RestA, 3);
                CoachLook.Stripes(p, x, y, w, h, CoachLook.RestB);
                text = new Color(0.67f, 0.71f, 0.8f);
            }
            else if (d == ready) { CoachLook.Box(p, x, y, w, h, CoachLook.Ready, 3); text = Color.white; bold = true; }
            else if (inPlan) CoachLook.Box(p, x, y, w, h, CoachLook.Plan, 3);
            else if (spare) { CoachLook.Frame(p, x, y, w, h, CoachLook.Spare, 1); text = CoachLook.Soft; }
            else text = CoachLook.Past;
            if (d == today) { CoachLook.Frame(p, x, y, w, h, Color.white, 2); bold = true; }
            CoachLook.Write(p, x, y, w - (d == eventDay ? 6 : 0), h, d.Day.ToString(CultureInfo.InvariantCulture), 15, text, bold, TextAlignmentOptions.Center);
            CoachLook.Hot(p, x, y, w, h, () => DayClick(d));
        }

        static void DayClick(DateTime d)
        {
            if (field == null) return; // nessun campo acceso: il calendario si guarda e basta
            DateTime start = Coach.Day(draft.start) ?? today, eventDay = Coach.Day(draft.eventDate) ?? today, ready = Coach.Day(draft.readyBy) ?? eventDay;
            switch (field)
            {
                case "start":
                    // un giorno passato va bene (contano le partite con la mod da quel giorno); al più un anno fa
                    if (d > ready || d < today.AddDays(-365)) return;
                    draft.start = Coach.Key(d);
                    draft.restDays.RemoveAll(r => Coach.Day(r) is DateTime x && x < d);
                    break;
                case "date":
                    if (d <= start || d < today) return; // l'evento non può essere già passato
                    draft.eventDate = Coach.Key(d);
                    // "ready by" segue l'evento (2 giorni prima) finché non lo scegli tu
                    if (!readyTouched || ready >= d) draft.readyBy = Coach.Key(d.AddDays(-2) < start ? start : d.AddDays(-2));
                    draft.restDays.RemoveAll(r => Coach.Day(r) is DateTime x && x >= d);
                    break;
                case "ready":
                    if (d < start || d > eventDay) return;
                    draft.readyBy = Coach.Key(d);
                    readyTouched = true;
                    break;
                case "rest":
                    if (d < start || d >= eventDay) return;
                    var key = Coach.Key(d);
                    if (!draft.restDays.Remove(key)) draft.restDays.Add(key);
                    break;
            }
            stopArmed = false;
            redraw = true;
        }

        static void StartClick()
        {
            draft.name = (nameField != null ? nameField.text : draft.name)?.Trim();
            var problem = editing ? CoachStore.Update(draft) : CoachStore.Start(draft);
            if (problem != null)
            {
                Plugin.Log.LogInfo("coach: evento non salvato (" + problem + ")");
                if (string.IsNullOrWhiteSpace(draft.name) && nameField != null) { nameField.Select(); nameField.ActivateInputField(); }
                return;
            }
            draft = null;
            tab = "today";
            redraw = true;
        }

        static void StopClick()
        {
            if (!stopArmed) { stopArmed = true; redraw = true; return; }
            CoachStore.Stop();
            draft = null;
            ResetSetup();
            redraw = true;
        }

        // i formati come li chiama il menu del sim (Standard, OP18, Extra Regulation…)
        static List<string> Formats()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            var list = new List<string>();
            foreach (MultiplayerStyle style in Enum.GetValues(typeof(MultiplayerStyle)))
            {
                if (style == MultiplayerStyle.Private) continue;
                var name = game != null ? Match.FormatName(game, style) : style.ToString();
                if (!list.Contains(name)) list.Add(name);
            }
            return list;
        }

        // i leader dei tuoi mazzi (la prima riga di ogni .deck) e quelli con cui hai giocato, i più usati prima
        static List<string> Leaders(List<CoachGame> games)
        {
            var count = new Dictionary<string, int>();
            try
            {
                foreach (var g in games.Where(g => g.myLeader != null && g.Kind != "solo"))
                    count[g.myLeader] = count.TryGetValue(g.myLeader, out var n) ? n + 1 : 1;
                var decks = Path.GetFullPath("Decks");
                if (Directory.Exists(decks))
                    foreach (var file in Directory.GetFiles(decks, "*.deck"))
                    {
                        var first = File.ReadLines(file).FirstOrDefault() ?? "";
                        int x = first.IndexOf('x');
                        var id = x >= 0 ? first.Substring(x + 1).Trim() : "";
                        if (id != "" && !count.ContainsKey(id)) count[id] = 0;
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("coach: leader non letti: " + e.Message); }
            if (draft?.leader != null && !count.ContainsKey(draft.leader)) count[draft.leader] = 0;
            return count.OrderByDescending(kv => kv.Value).ThenBy(kv => LeaderName(kv.Key)).Select(kv => kv.Key).ToList();
        }
    }
}
