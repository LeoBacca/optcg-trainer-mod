// Event Coach: le schede Today e Stats (disegni coach-c-today e coach-d-stats; le posizioni sono quelle dei loro HTML).
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
        const string Muted = "#9aa6c2", SoftHex = "#c9d1e3";

        // la lista delle partite di Today: l'esito si può mettere a mano (la connessione salta, il log resta a metà) e una
        // partita che la mod non ha registrato si può aggiungere (richiesta di Leo)
        static string rowEdit;       // la riga con i pulsanti dell'esito aperti (id della partita)
        static int listBack;         // di quante pagine (da 4) si è tornati indietro nella lista
        static bool listNoResult;    // la lista mostra le partite senza esito di tutto l'evento, non quelle di oggi
        static bool addOpen;         // il modulo "Add a game" al posto della lista
        static string addResult, addLeader;
        static bool addMine = true;  // la partita aggiunta è con il leader dell'evento (se l'evento ne ha uno)
        static List<string> addLeaders;

        static void ResetToday()
        {
            rowEdit = null;
            listBack = 0;
            listNoResult = addOpen = false;
        }

        /// Le partite che contano ma non hanno un esito (da segnare a mano).
        internal static int NoResult(CoachState st) => st.counted.Count(g => g.result == "o");

        // ---- TODAY ----

        static void BuildToday(RectTransform p, CoachState st)
        {
            // oggi
            Card(p, 184, 180, 820, 406);
            CoachLook.Caps(p, 208, 198, 480, "Today · " + st.today.ToString("ddd d MMM", CultureInfo.InvariantCulture));
            CoachLook.Write(p, 700, 197, 280, 22, $"the day ends at {Coach.DayStartsAt}:00", 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, 206, 206, 440, 128,
                st.todayTarget > 0 ? $"<size=100>{st.todayCount}</size><size=56><color={Muted}> / {st.todayTarget}</color></size><size=26><color={SoftHex}>  games</color></size>"
                    : $"<size=100>{st.todayCount}</size><size=26><color={SoftHex}>  {(st.todayCount == 1 ? "game" : "games")}</color></size>",
                26, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            var (top, under) = TodayMessage(st, false);
            CoachLook.Write(p, 600, 262, 380, 34, top, 25, Color.white, true, TextAlignmentOptions.TopRight, true);
            CoachLook.Write(p, 600, 296, 380, 26, under, 18, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            int n = Mathf.Min(st.todayTarget, 12);
            float seg = n > 0 ? (772 - 8 * (n - 1)) / (float)n : 0;
            for (int i = 0; i < n; i++)
                CoachLook.Box(p, 208 + i * (seg + 8), 352, seg, 16, i < st.todayCount ? CoachLook.Gold : CoachLook.Plan, 4);
            if (n == 0) CoachLook.Box(p, 208, 358, 772, 4, CoachLook.Plan, 2);

            // le partite senza esito: da qui si va a segnarle (anche quelle dei giorni passati)
            int noResult = NoResult(st);
            if (noResult == 0) listNoResult = false;
            if (listNoResult) Link(p, 700, 224, 280, "‹ back to today's games", CoachLook.Soft, () => { listNoResult = false; listBack = 0; rowEdit = null; });
            else if (noResult > 0) Link(p, 700, 224, 280, $"{Coach.N(noResult, "game")} without a result ›", CoachLook.Gold, () => { listNoResult = true; listBack = 0; rowEdit = null; addOpen = false; });

            if (addOpen) AddForm(p, st);
            else
            {
                // la prima pagina: le ultime 4 di oggi (o quelle senza esito). "earlier" va indietro di 4 in 4, anche nei giorni
                // prima, dall'inizio del piano: così un esito sbagliato o una partita aggiunta di ieri si possono ancora correggere
                var games = listNoResult ? st.counted.Where(g => g.result == "o").ToList()
                    : st.games.Where(g => g.day >= st.start && g.day <= st.today).OrderBy(g => g.startUtc).ToList();
                int first = Math.Min(4, listNoResult ? games.Count : st.todayGames.Count), before = games.Count - first;
                int pages = 1 + (before + 3) / 4;
                listBack = Mathf.Clamp(listBack, 0, pages - 1);
                int end = listBack == 0 ? games.Count : before - 4 * (listBack - 1), from = listBack == 0 ? before : Math.Max(0, end - 4);
                var shown = games.Skip(from).Take(end - from).ToList();
                if (shown.Count == 0) CoachLook.Write(p, 208, 388, 772, 26, "No games yet today.", 19, CoachLook.Dim);
                for (int i = 0; i < shown.Count; i++) GameRow(p, 382 + i * 34, shown[i], shown[i].day != st.today);
                if (listBack < pages - 1) Pill(p, 548, 541, 82, 28, "‹ earlier", CoachLook.Plan, CoachLook.Soft, () => { listBack++; rowEdit = null; });
                if (listBack > 0) Pill(p, 634, 541, 68, 28, "later ›", CoachLook.Plan, CoachLook.Soft, () => { listBack--; rowEdit = null; });
            }
            if (st.ev.leader != null)
            {
                var withLine = CoachLook.Write(p, 208, 546, 330, 24, $"With {LeaderName(st.ev.leader, st.leaderName)}: {st.withLeaderToday.games} of {st.todayRecord.games} · today {st.withLeaderToday.wins}–{st.withLeaderToday.losses}", 17, CoachLook.Label);
                withLine.overflowMode = TextOverflowModes.Ellipsis; // accanto ci sono i pulsanti della lista
            }
            if (!addOpen && !st.beforeStart)
                Pill(p, 706, 541, 104, 28, "+ ADD GAME", CoachLook.Plan, Color.white, () => { addOpen = true; listNoResult = false; addResult = null; addLeader = null; addLeaders = null; addMine = true; rowEdit = null; });
            CoachLook.Button(p, host.go_Settings, 818, 532, 162, 46, "PLAY", true, Play, 21);

            // i giorni che mancano
            Card(p, 1024, 180, 712, 116);
            CoachLook.Write(p, 1048, 180, 400, 82,
                st.daysToEvent > 0 ? $"<size=64>{st.daysToEvent}</size><size=24>  {(st.daysToEvent == 1 ? "day" : "days")} to the event</size>" : "<size=48>Event day</size>",
                24, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            string ready = st.behind ? $"Ready by was {Day(st.readyBy)} · {Coach.N(st.toGo, "game")} to go"
                : st.goalReached ? $"Goal reached: {st.total} / {st.goal} games"
                : $"Ready by {Day(st.readyBy)}: {Days(st.daysToReadyBy)} left · {Coach.N(st.toGo, "game")} to go";
            CoachLook.Write(p, 1048, 262, 560, 24, ready, 17, CoachLook.Label);
            CoachLook.Caps(p, 1420, 200, 292, "Daily target", 13, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, 1420, 220, 292, 26, st.rate + " · recalculated daily", 19, CoachLook.Soft, false, TextAlignmentOptions.TopRight);
            if (st.warning)
                CoachLook.Write(p, 1340, 246, 372, 22, "that's a lot: lower the goal or move Ready by", 15, CoachLook.Warn, false, TextAlignmentOptions.TopRight);

            // la serie
            Card(p, 1024, 312, 712, 274);
            CoachLook.Caps(p, 1048, 330, 300, "Streak");
            CoachLook.Write(p, 1400, 329, 312, 22, "rest days don't break it", 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            CoachLook.Picture(p, CoachLook.Flame, 1046, 358, 40, 50);
            CoachLook.Write(p, 1098, 346, 330, 66, $"<size=50>{st.streak} {(st.streak == 1 ? "day" : "days")}</size><size=18><color={Muted}>   best {st.bestStreak}</color></size>",
                18, Color.white, true, TextAlignmentOptions.BottomLeft, true);
            CoachLook.Caps(p, 1430, 376, 282, "Last 14 days", 13, TextAlignmentOptions.TopRight);
            for (int i = 0; i < 14; i++)
            {
                var date = st.today.AddDays(i - 13);
                float x = 1048 + i * 46;
                var day = st.days.FirstOrDefault(d => d.date == date);
                StripDay(p, x, 424, day);
                CoachLook.Write(p, x, 468, 40, 18, date.Day.ToString(CultureInfo.InvariantCulture), 13, CoachLook.Dim, false, TextAlignmentOptions.Top);
            }
            int next = Coach.StreakBadges.FirstOrDefault(b => !st.Badge(b));
            for (int i = 0; i < Coach.StreakBadges.Length; i++)
            {
                int b = Coach.StreakBadges[i];
                float x = 1048 + i * 172;
                bool earned = st.Badge(b), isNext = b == next;
                if (earned) CoachLook.Circle(p, x + 22, 526, 44, CoachLook.Gold);
                else if (isNext) { CoachLook.Circle(p, x + 22, 526, 44, CoachLook.Card); CoachLook.Circle(p, x + 22, 526, 44, CoachLook.Gold, true); }
                else CoachLook.Circle(p, x + 22, 526, 44, CoachLook.Plan);
                CoachLook.Write(p, x, 504, 44, 44, b.ToString(CultureInfo.InvariantCulture), 17, earned ? CoachLook.Dark : isNext ? Color.white : CoachLook.Dim, true, TextAlignmentOptions.Center);
                var sub = earned ? $"<color={Muted}>earned</color>" : isNext ? $"<color={Look.GoldHex}>{Math.Max(1, b - st.streak)} to go</color>" : $"<color={Muted}>locked</color>";
                CoachLook.Write(p, x + 54, 505, 118, 44, $"<b><color={(earned || isNext ? "#ffffff" : SoftHex)}>{b} days</color></b>\n{sub}", 16, Color.white);
            }

            // il pass
            Card(p, 184, 604, 1552, 162);
            string toNext = st.level >= Coach.Levels ? $"<color={Look.GoldHex}><b>EVENT READY</b></color>"
                : $"<color={Look.GoldHex}><b>{st.toNextLevel} {(st.toNextLevel == 1 ? "game" : "games")} to level {st.level + 1}</b></color>";
            CoachLook.Write(p, 208, 610, 1000, 36,
                $"<size=15><color={Muted}><b><cspace=0.14em>PASS</cspace></b></color></size>    <size=24><b>LEVEL {st.level} / {Coach.Levels}</b></size>    <size=19><color={SoftHex}>{st.total} / {st.goal} games</color></size>    <size=19>{toNext}</size>",
                15, Color.white, false, TextAlignmentOptions.BottomLeft);
            CoachLook.Write(p, 1180, 620, 532, 22, $"one level every {LevelStep(st.goal)} · skipping a day loses nothing", 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            PassTrack(p, st);
        }

        static string LevelStep(int goal)
        {
            int lo = goal / Coach.Levels, hi = (goal + Coach.Levels - 1) / Coach.Levels;
            if (hi <= 1) return "game";
            return lo == hi ? $"{lo} games" : $"{Math.Max(1, lo)}–{hi} games";
        }

        // una riga della lista: l'esito, l'avversario, i numeri, l'ora (o il giorno, nella lista delle partite senza esito).
        // Senza esito: WON / LOST lì accanto. Con un esito: "edit" apre gli stessi pulsanti (il log può sbagliare).
        static void GameRow(RectTransform p, float y, CoachGame g, bool withDay)
        {
            // si correggono le partite che contano e quelle tolte con DON'T COUNT; quelle dell'estensione di Chrome no (si
            // correggono lì: qui arrivano dal sito)
            bool counts = g.why == null, editable = (counts || g.excluded) && !g.remote;
            bool open = editable && rowEdit == g.ids[0], unknown = counts && g.result == "o";
            var chip = !counts ? CoachLook.Unknown : g.result == "w" ? CoachLook.Win : g.result == "l" ? CoachLook.Loss : CoachLook.Unknown;
            CoachLook.Box(p, 208, y + 4, 28, 28, chip, 4);
            CoachLook.Write(p, 208, y + 4, 28, 28, counts && g.result == "w" ? "W" : counts && g.result == "l" ? "L" : unknown ? "?" : "–", 16, counts ? Color.white : CoachLook.Soft, true, TextAlignmentOptions.Center);
            var color = counts ? CoachLook.Text : CoachLook.Dim;
            var vs = CoachLook.Write(p, 250, y + 6, 272, 26, "vs " + (g.oppLeader == null ? "unknown leader" : LeaderName(g.oppLeader, g.oppLeaderName)), 19, color);
            vs.overflowMode = TextOverflowModes.Ellipsis;
            void Set(string result) { CoachStore.SetResult(g, result); rowEdit = null; }
            if (open || (unknown && !g.remote))
            {
                // WON · LOST · DON'T COUNT (0.5.0: la partita esce dal conto e dal winrate), e UNDO o REMOVE
                float x = 530;
                bool lit(string r) => g.excluded ? r == "x" : g.result == r;
                Pill(p, x, y + 4, 66, 28, "WON", lit("w") ? CoachLook.Win : CoachLook.Plan, Color.white, () => Set("w"));
                Pill(p, x + 70, y + 4, 66, 28, "LOST", lit("l") ? CoachLook.Loss : CoachLook.Plan, Color.white, () => Set("l"));
                x += 140;
                // una partita aggiunta a mano non si toglie dal conto: si toglie e basta (REMOVE)
                if (!g.added) { Pill(p, x, y + 4, 116, 28, "DON'T COUNT", lit("x") ? CoachLook.Gold : CoachLook.Plan, lit("x") ? CoachLook.Dark : Color.white, () => Set("x")); x += 120; }
                // di nuovo come dice il log (se l'esito era stato messo a mano), o via la partita aggiunta a mano
                if (open && g.added) Pill(p, x, y + 4, 84, 28, "REMOVE", CoachLook.Plan, CoachLook.Warn, () => { CoachStore.RemoveAdded(g); rowEdit = null; });
                else if (open && g.byHand) Pill(p, x, y + 4, 84, 28, "UNDO", CoachLook.Plan, CoachLook.Soft, () => Set(null));
            }
            else if (g.remote)
            {
                // dall'estensione di Chrome: un'etichetta con il sim dove è stata giocata (o "web"), poi turni e primo/secondo se si sanno
                var where = Coach.Short(g.sim ?? "web", 16);
                float w = Mathf.Clamp(where.Length * 9 + 18, 46, 160);
                CoachLook.Box(p, 530, y + 7, w, 22, CoachLook.Spare, 4);
                CoachLook.Write(p, 530, y + 7, w, 22, "<noparse>" + where.Replace("</noparse>", "") + "</noparse>", 13, CoachLook.Soft, true, TextAlignmentOptions.Center);
                string det = string.Join(" · ", new[] { g.turns > 0 ? g.turns + " turns" : null, g.first == 1 ? "went first" : g.first == 2 ? "went second" : null, "by hand" }.Where(s => s != null));
                CoachLook.Write(p, 530 + w + 10, y + 8, 300 - w, 24, det, 17, CoachLook.Label).overflowMode = TextOverflowModes.Ellipsis;
            }
            else
            {
                string det = !counts ? NotCounted(g.why)
                    : g.added ? "added by hand"
                    : $"{g.turns} turns · {(g.minutes != null ? (g.minutes.Value < 0.5 ? "<1" : Math.Round(g.minutes.Value).ToString(CultureInfo.InvariantCulture)) + " min · " : "")}{(g.first == 1 ? "went first" : g.first == 2 ? "went second" : "")}".TrimEnd(' ', '·')
                        + (g.byHand ? " · by hand" : "");
                CoachLook.Write(p, 530, y + 8, 300, 24, det, 17, counts ? CoachLook.Label : CoachLook.Dim).overflowMode = TextOverflowModes.Ellipsis;
            }
            // aperta, i pulsanti arrivano fin qui: l'ora si toglie (torna chiudendo)
            if (!open) CoachLook.Write(p, 836, y + 9, 86, 22, g.startLocal.ToString(withDay ? "d MMM" : "HH:mm", CultureInfo.InvariantCulture), 16, CoachLook.Dim, false, TextAlignmentOptions.TopRight);
            if (open) Pill(p, 932, y + 4, 48, 28, "close", CoachLook.Card, CoachLook.Label, () => rowEdit = null);
            else if (editable && !unknown) Pill(p, 932, y + 4, 48, 28, "edit", CoachLook.Card, CoachLook.Label, () => rowEdit = g.ids[0]);
        }

        // "Add a game", al posto della lista: una partita che la mod non ha registrato (sim aperto senza la mod, mod rotta da un
        // aggiornamento). Si sceglie l'esito e, se si vuole, il leader dell'avversario; vale per oggi.
        static void AddForm(RectTransform p, CoachState st)
        {
            CoachLook.Caps(p, 208, 386, 500, "Add a game the mod didn't record", 13);
            CoachLook.Write(p, 208, 420, 100, 24, "Result", 17, CoachLook.Soft);
            Pill(p, 316, 414, 92, 34, "WON", addResult == "w" ? CoachLook.Win : CoachLook.Plan, Color.white, () => addResult = "w", 16);
            Pill(p, 414, 414, 92, 34, "LOST", addResult == "l" ? CoachLook.Loss : CoachLook.Plan, Color.white, () => addResult = "l", 16);
            CoachLook.Write(p, 208, 466, 100, 24, "Opponent", 17, CoachLook.Soft);
            addLeaders ??= OpponentLeaders();
            var names = new List<string> { "Unknown leader" };
            names.AddRange(addLeaders.Select(id => $"{LeaderName(id)} · {id}"));
            Dropdown(p, 316, 462, 330, names, Math.Max(0, addLeaders.IndexOf(addLeader) + 1), i => addLeader = i == 0 ? null : addLeaders[i - 1], 17);
            // con il leader dell'evento, di base sì (si toglie se quella partita l'hai giocata con un altro mazzo)
            string mine = st.ev.leader != null && addMine ? st.ev.leader : null;
            if (st.ev.leader != null) Check(p, 668, 458, "with " + Coach.Short(LeaderName(st.ev.leader, st.leaderName), 22), addMine, on => addMine = on);
            CoachLook.Write(p, 208, 508, 600, 22, "It counts for today, like a game played now.", 15, CoachLook.Dim);
            if (addResult != null)
                CoachLook.Button(p, host.go_Settings, 690, 412, 140, 40, "ADD", true, () => { CoachStore.AddGame(addResult, addLeader, mine); addOpen = false; listBack = 0; listNoResult = false; redraw = true; }, 18);
            else CoachLook.Write(p, 660, 420, 200, 24, "pick the result", 15, CoachLook.Dim);
            Pill(p, 846, 414, 84, 34, "cancel", CoachLook.Card, CoachLook.Label, () => addOpen = false, 15);
        }

        // i leader per "Add a game": prima quelli già incontrati (i più frequenti), poi tutti gli altri del database delle carte
        static List<string> OpponentLeaders()
        {
            var met = new Dictionary<string, int>();
            try
            {
                foreach (var g in CoachStore.Games().Where(g => g.oppLeader != null && g.Kind != "solo"))
                    met[g.oppLeader] = met.TryGetValue(g.oppLeader, out var n) ? n + 1 : 1;
                var db = CardDatabaseScript.Instance;
                if (db != null && db.dict_Cards != null)
                    foreach (var card in db.dict_Cards)
                        if (card.Value != null && card.Value.cardDefinition != null && card.Value.cardDefinition.cardType == CardType.Leader && !met.ContainsKey(card.Key))
                            met[card.Key] = 0;
            }
            catch (Exception e) { Plugin.Log.LogWarning("coach: leader non letti: " + e.Message); }
            return met.OrderByDescending(kv => kv.Value).ThenBy(kv => LeaderName(kv.Key)).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToList();
        }

        // un pulsante piccolo dei nostri (i pulsanti di Batsu così piccoli hanno la cornice troppo spessa): fondo tondo e scritta
        static void Pill(RectTransform p, float x, float y, float w, float h, string label, Color fill, Color text, Action click, float size = 14)
        {
            CoachLook.Box(p, x, y, w, h, fill, 4);
            CoachLook.Write(p, x, y, w, h, label, size, text, true, TextAlignmentOptions.Center);
            CoachLook.Hot(p, x, y, w, h, () => { click(); redraw = true; });
        }

        // una scritta che si clicca, allineata a destra
        static void Link(RectTransform p, float x, float y, float w, string label, Color color, Action click)
        {
            CoachLook.Write(p, x, y, w, 22, label, 15, color, false, TextAlignmentOptions.TopRight);
            CoachLook.Hot(p, x + w - 240, y - 3, 240, 28, () => { click(); redraw = true; });
        }

        // un giorno degli ultimi 14: oro con la spunta = fatto, × = mancato, a righe = riposo, oggi con il bordo e quanto manca
        static void StripDay(RectTransform p, float x, float y, CoachDay d)
        {
            const float s = 40;
            if (d == null) { CoachLook.Frame(p, x, y, s, s, CoachLook.Plan, 1); return; } // prima del piano
            if (d.rest)
            {
                CoachLook.Box(p, x, y, s, s, CoachLook.RestA, 4);
                CoachLook.Stripes(p, x, y, s, s, CoachLook.RestB);
            }
            else if (d.Met)
            {
                CoachLook.Box(p, x, y, s, s, CoachLook.Gold, 4);
                CoachLook.Picture(p, CoachLook.Check, x + 9, y + 11, 22, 18);
            }
            else
            {
                CoachLook.Box(p, x, y, s, s, CoachLook.Plan, 4);
                if (d.isToday && d.target > 0)
                {
                    float f = Mathf.Clamp01(d.record.games / (float)d.target);
                    if (f > 0) CoachLook.Box(p, x, y + s * (1 - f), s, s * f, CoachLook.PartGold, 4);
                    CoachLook.Write(p, x, y, s, s, $"{d.record.games}/{d.target}", 12, Color.white, true, TextAlignmentOptions.Center);
                }
                else if (!d.isToday && d.target > 0) CoachLook.Write(p, x, y, s, s, "×", 18, new Color(0.49f, 0.54f, 0.66f), false, TextAlignmentOptions.Center);
            }
            if (d.isToday) CoachLook.Frame(p, x, y, s, s, Color.white, 2);
        }

        static void PassTrack(RectTransform p, CoachState st)
        {
            const float y = 694, x0 = 226, dx = 74.6f;
            float X(int k) => x0 + (k - 1) * dx;
            CoachLook.Box(p, X(1), y - 2, X(20) - X(1), 4, CoachLook.Plan, 2);
            float done = st.level >= Coach.Levels ? X(20) - X(1) : 0;
            if (st.level < Coach.Levels && st.level > 0)
            {
                int from = Coach.GamesForLevel(st.level, st.goal), to = Coach.GamesForLevel(st.level + 1, st.goal);
                float frac = to > from ? Mathf.Clamp01((st.total - from) / (float)(to - from)) : 0;
                done = X(st.level) - X(1) + dx * frac;
            }
            if (done > 0) CoachLook.Box(p, X(1), y - 2, done, 4, CoachLook.Gold, 2);
            for (int k = 1; k < Coach.Levels; k++)
            {
                float d = k % 5 == 0 ? 38 : 32;
                bool isDone = k <= st.level, isNext = k == st.level + 1;
                if (isDone) CoachLook.Circle(p, X(k), y, d, CoachLook.Gold);
                else if (isNext) { CoachLook.Circle(p, X(k), y, d, CoachLook.Card); CoachLook.Circle(p, X(k), y, d, CoachLook.Gold, true); }
                else CoachLook.Circle(p, X(k), y, d, CoachLook.Plan);
                CoachLook.Write(p, X(k) - d / 2, y - d / 2, d, d, k.ToString(CultureInfo.InvariantCulture), 14, isDone ? CoachLook.Dark : isNext ? Color.white : CoachLook.Dim, true, TextAlignmentOptions.Center);
            }
            // l'ultimo livello: EVENT READY
            bool ready = st.level >= Coach.Levels;
            CoachLook.Circle(p, X(20), y, 54, ready ? CoachLook.Gold : CoachLook.Plan);
            if (!ready) CoachLook.Circle(p, X(20), y, 54, CoachLook.Gold, true);
            CoachLook.Picture(p, CoachLook.Trophy, X(20) - 15, y - 15, 30, 30, ready ? CoachLook.Dark : (Color?)null);
            foreach (var (k, title) in new[] { (5, "bronze"), (10, "silver"), (15, "gold") })
            {
                int g = Coach.GamesForLevel(k, st.goal);
                CoachLook.Write(p, X(k) - 100, 740, 200, 20, k == 5 ? $"{g} games · {title}" : $"{g} · {title}", 14, CoachLook.Label, false, TextAlignmentOptions.Top);
            }
            CoachLook.Write(p, X(20) - 140, 740, 200, 20, "EVENT READY · " + st.goal, 15, CoachLook.Gold, true, TextAlignmentOptions.Top);
        }

        // PLAY: chiude il coach e apre Multiplayer, come il pulsante di Batsu
        static void Play()
        {
            Close();
            var b = host != null && host.go_Multiplayer != null ? host.go_Multiplayer.GetComponent<Button>() : null;
            if (b != null && b.isActiveAndEnabled && b.interactable) b.onClick.Invoke();
        }

        // ---- STATS ----
        // 0.5.0 (richiesta di Leo: "capire quali mazzi che ho giocato io stanno performando"): al centro YOUR DECKS sopra i
        // MATCHUPS, e un clic su un mio mazzo filtra i matchup su quel mazzo. Per farci stare tutto senza togliere niente: i 4
        // numeri grandi più bassi, il calendario e le settimane un poco più stretti, le righe delle tabelle da 30.

        static string deckPick;   // il mazzo scelto: null = quello dell'evento (se c'è, se no tutti), "" = tutti, se no il leader ("?" = senza)
        static GameObject tip;

        // il mazzo dei matchup adesso: null = tutti
        static string PickedDeck(CoachState st)
        {
            var pick = deckPick ?? st.ev.leader ?? "";
            return pick == "" ? null : pick;
        }

        const float KpiY = 180, KpiH = 92, LowY = 288, LowH = 478;   // le due file di riquadri (fino a 766, come prima)
        const float HeatX = 184, HeatW = 500, MidX = 700, MidW = 690, WeekX = 1406, WeekW = 330;

        static void BuildStats(RectTransform p, CoachState st)
        {
            string hours = st.totalMinutes >= 60 ? Math.Round(st.totalMinutes / 60) + " h played" : Math.Round(st.totalMinutes) + " min played";
            string Rate(Record r) => r.Percent != null ? r.Percent + "%" : "–";
            var kpis = new[]
            {
                ("Games", st.total.ToString(CultureInfo.InvariantCulture), $"of {st.goal} · {hours}"),
                ("Win rate", Rate(st.all), $"{st.all.wins}–{st.all.losses}" + (st.all.Unknown > 0 ? $" · {st.all.Unknown} unfinished not counted" : "")),
                ("Going first", Rate(st.goingFirst), $"{st.goingFirst.wins}–{st.goingFirst.losses} · {Coach.N(st.goingFirst.games, "game")}"),
                ("Going second", Rate(st.goingSecond), $"{st.goingSecond.wins}–{st.goingSecond.losses} · {Coach.N(st.goingSecond.games, "game")}"),
            };
            for (int i = 0; i < kpis.Length; i++)
            {
                float x = 184 + i * 392;
                Card(p, x, KpiY, 376, KpiH);
                CoachLook.Caps(p, x + 24, KpiY + 10, 330, kpis[i].Item1, 14);
                CoachLook.Write(p, x + 24, KpiY + 24, 330, 40, kpis[i].Item2, 34, Color.white, true, TextAlignmentOptions.BottomLeft);
                CoachLook.Write(p, x + 24, KpiY + 66, 340, 22, kpis[i].Item3, 16, CoachLook.Label);
            }
            Heatmap(p, st);
            DecksAndMatchups(p, st);
            Weeks(p, st);
        }

        // Le settimane da mostrare, le stesse nel calendario a quadratini e nel grafico per settimana: tutte, da quella di
        // "Start from" a quella dell'evento, fino a 10. Se sono di più: 7 prima di questa, questa e 2 dopo, e si scrive quali.
        const int MaxWeeks = 10;

        static (int first, int count) WeekWindow(CoachState st)
        {
            int n = st.weeks.Count;
            if (n <= MaxWeeks) return (0, n);
            int cur = st.over ? n - 1 : st.beforeStart ? 0 : Math.Max(0, st.weeks.FindLastIndex(w => w.monday <= st.today));
            return (Mathf.Clamp(cur - (MaxWeeks - 3), 0, n - MaxWeeks), MaxWeeks);
        }

        // sotto il titolo, solo se non si vedono tutte: "26 Oct – 4 Jan · 10 of 14 weeks"
        static void WeekRange(RectTransform p, float x, CoachState st, int first, int count)
        {
            if (count >= st.weeks.Count) return;
            var from = st.weeks[first].monday;
            var to = st.weeks[first + count - 1].monday.AddDays(6);
            CoachLook.Write(p, x, LowY + 37, 300, 18, $"{from.ToString("d MMM", CultureInfo.InvariantCulture)} – {to.ToString("d MMM", CultureInfo.InvariantCulture)} · {count} of {st.weeks.Count} weeks",
                13, CoachLook.Dim);
        }

        static void Heatmap(RectTransform p, CoachState st)
        {
            Card(p, HeatX, LowY, HeatW, LowH);
            CoachLook.Caps(p, 208, LowY + 18, 220, "Games per day");
            // la scala
            CoachLook.Write(p, 388, LowY + 17, 32, 22, "less", 14, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            string[] bins = { "0", "1–2", "3–4", "5–6", "7+" };
            for (int i = 0; i < 5; i++)
            {
                CoachLook.Box(p, 424 + i * 38, LowY + 16, 34, 22, CoachLook.Bins[i], 3);
                CoachLook.Write(p, 424 + i * 38, LowY + 16, 34, 22, bins[i], 13, i == 0 ? CoachLook.Soft : i == 1 ? Color.white : CoachLook.Dark, true, TextAlignmentOptions.Center);
            }
            CoachLook.Write(p, 618, LowY + 17, 50, 22, "more", 14, CoachLook.Label);
            var (first, count) = WeekWindow(st);
            WeekRange(p, 208, st, first, count);

            // una colonna per settimana (fino a 10), una riga per giorno; la legenda sotto
            const float X0 = 252, Y0 = LowY + 80, S = 42, C = 38;
            string[] names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int r = 0; r < 7; r++) CoachLook.Write(p, 208, Y0 + r * S, 40, C, names[r], 13, CoachLook.Dim, false, TextAlignmentOptions.MidlineLeft);
            var byDate = st.days.ToDictionary(d => d.date);
            for (int c = 0; c < count; c++)
            {
                var monday = st.weeks[first + c].monday;
                // il nome del mese sopra la prima colonna e dove il mese cambia (il mese di una colonna è quello del suo lunedì)
                if (c == 0 || monday.Month != st.weeks[first + c - 1].monday.Month)
                    CoachLook.Write(p, X0 + c * S, Y0 - 22, 60, 18, monday.ToString("MMM", CultureInfo.InvariantCulture), 13, CoachLook.Label);
                for (int r = 0; r < 7; r++)
                    if (byDate.TryGetValue(monday.AddDays(r), out var day)) HeatCell(p, X0 + c * S, Y0 + r * S, C, day);
            }
            var items = new (string text, Action<float, float> draw)[]
            {
                ("goal met", (x, y) => { CoachLook.Box(p, x, y, 20, 20, CoachLook.Bins[3], 3); CoachLook.Picture(p, CoachLook.Check, x + 3, y + 4, 14, 12); }),
                ("rest day", (x, y) => { CoachLook.Box(p, x, y, 20, 20, CoachLook.RestA, 3); CoachLook.Stripes(p, x, y, 20, 20, CoachLook.RestB); }),
                ("today", (x, y) => { CoachLook.Box(p, x, y, 20, 20, CoachLook.Bins[2], 3); CoachLook.Frame(p, x, y, 20, 20, Color.white, 2); }),
                ("to come", (x, y) => CoachLook.Frame(p, x, y, 20, 20, CoachLook.Outline, 1)),
                ("ready by", (x, y) => CoachLook.Frame(p, x, y, 20, 20, CoachLook.ReadyLine, 2)),
                ("event", (x, y) => { CoachLook.Frame(p, x, y, 20, 20, CoachLook.Gold, 2); CoachLook.Picture(p, CoachLook.Star, x + 3, y + 3, 14, 14, CoachLook.Gold); }),
            };
            for (int i = 0; i < items.Length; i++)
            {
                float x = 208 + (i % 3) * 156, y = LowY + 402 + (i / 3) * 30;
                items[i].draw(x, y + 2);
                CoachLook.Write(p, x + 28, y, 124, 24, items[i].text, 15, CoachLook.Soft, false, TextAlignmentOptions.MidlineLeft);
            }
        }

        static void HeatCell(RectTransform p, float x, float y, float s, CoachDay d)
        {
            float icon = s * 0.5f;
            if (d.future)
            {
                if (d.rest) { CoachLook.Box(p, x, y, s, s, CoachLook.RestA, 4); CoachLook.Stripes(p, x, y, s, s, new Color(0.2f, 0.26f, 0.42f)); }
                if (d.eventDay) { CoachLook.Frame(p, x, y, s, s, CoachLook.Gold, 2.5f); CoachLook.Picture(p, CoachLook.Star, x + (s - icon) / 2, y + (s - icon) / 2, icon, icon, CoachLook.Gold); }
                else if (d.readyBy) CoachLook.Frame(p, x, y, s, s, CoachLook.ReadyLine, 2.5f);
                else CoachLook.Frame(p, x, y, s, s, CoachLook.Outline, 1);
            }
            else
            {
                CoachLook.Box(p, x, y, s, s, CoachLook.Bins[CoachLook.Bin(d.record.games)], 4);
                if (d.rest) CoachLook.Stripes(p, x, y, s, s, new Color(0.08f, 0.11f, 0.2f, 0.55f));
                if (d.Met) CoachLook.Picture(p, CoachLook.Check, x + (s - icon) / 2, y + (s - icon * 0.8f) / 2, icon, icon * 0.8f);
                if (d.eventDay) CoachLook.Picture(p, CoachLook.Star, x + (s - icon) / 2, y + (s - icon) / 2, icon, icon, CoachLook.Gold);
                if (d.isToday) CoachLook.Frame(p, x, y, s, s, Color.white, 2.5f);
            }
            CoachLook.Hot(p, x, y, s, s, null, () => ShowTip(p, x + s + 8, y - 2, d), HideTip);
        }

        // il riquadro che segue il mouse sul calendario: giorno, partite, numero
        static void ShowTip(RectTransform p, float x, float y, CoachDay d)
        {
            HideTip();
            string line = d.eventDay ? "event day" : d.rest ? "rest day" : d.future ? (d.readyBy ? "ready by" : "to come")
                : d.isToday ? (d.target > 0 ? $"today: {d.record.games} of {d.target}" : "today")
                : d.target == 0 ? "nothing to do" : d.Met ? $"goal met ({d.target})" : $"goal {d.target} · missed";
            string games = d.future ? "" : $"{d.record.games} {(d.record.games == 1 ? "game" : "games")} · {d.record.wins}–{d.record.losses}";
            var rt = CoachLook.At(p, "Tip", x, y, 190, games == "" ? 58 : 82);
            tip = rt.gameObject;
            CoachLook.Box(rt, 0, 0, 190, rt.sizeDelta.y, CoachLook.Tip, 4);
            CoachLook.Frame(rt, 0, 0, 190, rt.sizeDelta.y, CoachLook.Outline, 1);
            CoachLook.Write(rt, 14, 9, 170, 24, d.date.ToString("ddd d MMM", CultureInfo.InvariantCulture), 17, Color.white, true);
            if (games != "") CoachLook.Write(rt, 14, 33, 170, 22, games, 16, CoachLook.Soft);
            CoachLook.Write(rt, 14, games == "" ? 33 : 55, 170, 22, line, 16, d.Met ? CoachLook.Gold : CoachLook.Soft, d.Met);
            rt.SetAsLastSibling();
        }

        static void HideTip()
        {
            if (tip != null) UnityEngine.Object.Destroy(tip);
            tip = null;
        }

        // le colonne delle due tabelle del riquadro di mezzo (YOUR DECKS e MATCHUPS), allineate
        const float ColName = 724, ColGames = 976, ColWl = 1050, ColBar = 1214, ColWin = 1296, ColEnd = 1366, RowH = 30;

        static void DecksAndMatchups(RectTransform p, CoachState st)
        {
            Card(p, MidX, LowY, MidW, LowH);
            var picked = PickedDeck(st);

            // YOUR DECKS: i leader con cui ho giocato, al più 5 righe (poi "Others"); quello scelto sempre in vista
            CoachLook.Caps(p, ColName, LowY + 18, 200, "Your decks");
            Heads(p, LowY + 20);
            const int MaxDecks = 5, MaxMatchups = 7;
            var decks = st.myDecks.Take(st.myDecks.Count > MaxDecks ? MaxDecks - 1 : MaxDecks).ToList();
            var chosen = picked != null ? st.myDecks.FirstOrDefault(d => string.Equals(d.leader, picked, StringComparison.OrdinalIgnoreCase)) : null;
            if (chosen != null && !decks.Contains(chosen)) decks[decks.Count - 1] = chosen;
            float y = LowY + 44;
            if (st.myDecks.Count == 0) CoachLook.Write(p, ColName, y + 4, 600, 24, "No games yet.", 17, CoachLook.Dim);
            for (int i = 0; i < decks.Count; i++)
            {
                var d = decks[i];
                bool on = d == chosen;
                if (on) { CoachLook.Box(p, ColName - 12, y + i * RowH + 1, ColEnd - ColName + 24, RowH - 2, CoachLook.Plan, 4); CoachLook.Box(p, ColName - 12, y + i * RowH + 1, 4, RowH - 2, CoachLook.Gold, 2); }
                Row(p, y + i * RowH, DeckName(d.leader, d.name), d.record, d.Few, on);
                var leader = d.leader;
                CoachLook.Hot(p, ColName - 12, y + i * RowH, ColEnd - ColName + 24, RowH, () => { deckPick = on ? "" : leader; redraw = true; });
            }
            if (st.myDecks.Count > MaxDecks)
            {
                var rest = new Record();
                foreach (var d in st.myDecks.Where(d => !decks.Contains(d))) { rest.games += d.record.games; rest.wins += d.record.wins; rest.losses += d.record.losses; }
                Row(p, y + decks.Count * RowH, $"Others ({st.myDecks.Count - decks.Count} decks)", rest, rest.wins + rest.losses < Coach.MinMatchupGames, false);
            }

            // MATCHUPS: dell'evento, o di un mio mazzo
            float my = LowY + 208;
            CoachLook.Box(p, ColName, my - 12, ColEnd - ColName, 1, CoachLook.CardLine);
            CoachLook.Caps(p, ColName, my, 200, "Matchups");
            var list = Coach.MatchupsWith(st, picked);
            int games = list.Sum(m => m.record.games);
            // senza un mazzo scelto si dice che i mazzi lassù si cliccano
            var with = picked != null ? "with " + DeckName(picked, chosen?.name) + " · " + Coach.N(games, "game")
                : Coach.N(games, "game") + " · all your decks" + (st.myDecks.Count > 1 ? " · click a deck above" : "");
            CoachLook.Write(p, ColName + 128, my, picked != null ? 400 : 510, 22, with, 15, CoachLook.Label).overflowMode = TextOverflowModes.Ellipsis;
            if (picked != null) Pill(p, ColEnd - 104, my - 6, 104, 28, "All decks", CoachLook.Plan, Color.white, () => deckPick = "", 14);
            float ry = my + 26;
            if (list.Count == 0) CoachLook.Write(p, ColName, ry + 4, 542, 26, "No games yet.", 17, CoachLook.Dim);
            // al più 7 righe: le ultime diventano "Others (N leaders)"
            var rows = list.Take(list.Count > MaxMatchups ? MaxMatchups - 1 : MaxMatchups).Select(m => ("vs " + LeaderName(m.leader, m.name), m.record, m.Few)).ToList();
            if (list.Count > MaxMatchups)
            {
                var rest = new Record();
                foreach (var m in list.Skip(MaxMatchups - 1)) { rest.games += m.record.games; rest.wins += m.record.wins; rest.losses += m.record.losses; }
                rows.Add(($"Others ({list.Count - MaxMatchups + 1} leaders)", rest, rest.wins + rest.losses < Coach.MinMatchupGames));
            }
            for (int i = 0; i < rows.Count; i++) Row(p, ry + i * RowH, rows[i].Item1, rows[i].record, rows[i].Few, false, i < rows.Count - 1);
            CoachLook.Box(p, ColName, LowY + LowH - 23, 22, 12, CoachLook.Win, 3);
            CoachLook.Write(p, ColName + 30, LowY + LowH - 28, 120, 22, "you win more", 14, CoachLook.Soft);
            CoachLook.Box(p, ColName + 146, LowY + LowH - 23, 22, 12, CoachLook.Loss, 3);
            CoachLook.Write(p, ColName + 176, LowY + LowH - 28, 120, 22, "you lose more", 14, CoachLook.Soft);
            CoachLook.Write(p, ColName + 300, LowY + LowH - 28, ColEnd - ColName - 300, 22, "– = fewer than 3 games with a result", 14, CoachLook.Label, false, TextAlignmentOptions.TopRight);
        }

        // "Red-Green Luffy"; senza leader "Unknown leader"
        static string DeckName(string leader, string name) => leader == null || leader == "?" ? "Unknown leader" : LeaderName(leader, name);

        // i titoli delle colonne, sulla riga del titolo della tabella
        static void Heads(RectTransform p, float y)
        {
            CoachLook.Write(p, ColGames - 10, y, 80, 20, "<cspace=0.06em>GAMES</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, ColWl, y, 74, 20, "<cspace=0.06em>W–L</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, ColBar - 30, y, 60, 20, "50%", 13, CoachLook.Dim, false, TextAlignmentOptions.Top);
            CoachLook.Write(p, ColWin, y, ColEnd - ColWin, 20, "<cspace=0.06em>WIN</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
        }

        // una riga: nome, partite, W–L, la barra (blu: vinci di più, rossa: perdi di più, lunga fino a ±25 punti), il winrate
        // ("–" con meno di 3 partite con un esito)
        static void Row(RectTransform p, float y, string name, Record r, bool few, bool on, bool line = false)
        {
            var c = few ? CoachLook.Dim : CoachLook.Text;
            CoachLook.Write(p, ColName, y + 4, ColGames - ColName - 8, 24, name, 17, on ? Color.white : c, on).overflowMode = TextOverflowModes.Ellipsis;
            CoachLook.Write(p, ColGames, y + 5, 70, 22, r.games.ToString(CultureInfo.InvariantCulture), 17, few ? CoachLook.Dim : CoachLook.Soft, false, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, ColWl, y + 5, 74, 22, $"{r.wins}–{r.losses}", 17, few ? CoachLook.Dim : CoachLook.Soft, false, TextAlignmentOptions.TopRight);
            CoachLook.Box(p, ColBar, y + 4, 1, 22, CoachLook.Outline);
            if (!few && r.Percent is int pct)
            {
                float px = Mathf.Max(Mathf.Min(Mathf.Abs(pct - 50), 25) * 70f / 25f, 2);
                var color = pct > 50 ? CoachLook.Win : pct < 50 ? CoachLook.Loss : CoachLook.Even;
                CoachLook.Box(p, pct >= 50 ? ColBar + 1 : ColBar - px, y + 9, px, 12, color, 3);
            }
            CoachLook.Write(p, ColWin, y + 5, ColEnd - ColWin, 22, few || r.Percent == null ? "–" : r.Percent + "%", 17, few ? CoachLook.Dim : Color.white, !few, TextAlignmentOptions.TopRight);
            if (line) CoachLook.Box(p, ColName, y + RowH - 1, ColEnd - ColName, 1, CoachLook.Row);
        }

        static void Weeks(RectTransform p, CoachState st)
        {
            Card(p, WeekX, LowY, WeekW, LowH);
            CoachLook.Caps(p, WeekX + 24, LowY + 18, 280, "Games per week");
            var (first, count) = WeekWindow(st);
            WeekRange(p, WeekX + 24, st, first, count);
            var shown = st.weeks.Skip(first).Take(count).ToList();
            if (shown.Count == 0) return;
            const float B = 690, Left = WeekX + 24, Width = WeekW - 48;
            int max = Math.Max(1, shown.Max(w => Math.Max(w.games, w.plan)));
            float K = Mathf.Min(6.4f, 280f / max);
            float step = Width / shown.Count, bar = Mathf.Min(40, step * 0.62f), tick = Mathf.Min(54, step * 0.86f);
            bool roomy = shown.Count <= 6; // con tante settimane: numeri più piccoli, niente "so far", una data sì e una no
            int cur = shown.FindIndex(w => w.current);
            CoachLook.Box(p, Left, B, Width, 1, CoachLook.Outline);
            for (int i = 0; i < shown.Count; i++)
            {
                var w = shown[i];
                float cx = Left + step * (i + 0.5f);
                if (w.future && w.games == 0)
                {
                    if (w.plan > 0) CoachLook.Frame(p, cx - bar / 2, B - w.plan * K, bar, w.plan * K, CoachLook.Outline, 1);
                }
                else
                {
                    if (w.games > 0) CoachLook.Box(p, cx - bar / 2, B - w.games * K, bar, w.games * K, CoachLook.Bins[3], 3);
                    var value = w.current && roomy ? $"{w.games} so far" : w.games.ToString(CultureInfo.InvariantCulture);
                    CoachLook.Write(p, cx - 40, B - w.games * K - (roomy ? 26 : 22), 80, 22, value, roomy ? 16 : 13, Color.white, true, TextAlignmentOptions.Top);
                }
                if (w.plan > 0) CoachLook.Box(p, cx - tick / 2, B - w.plan * K - 1, tick, 2, new Color(1, 1, 1, 0.8f));
                bool label = roomy || (cur >= 0 ? (i - cur) % 2 == 0 : i % 2 == 0);
                if (label)
                    CoachLook.Write(p, cx - 34, B + 8, 68, 20, w.monday.ToString("d MMM", CultureInfo.InvariantCulture), roomy ? 14 : 12, w.current ? Color.white : CoachLook.Dim, w.current, TextAlignmentOptions.Top);
            }
            CoachLook.Box(p, WeekX + 24, 749, 22, 2, new Color(1, 1, 1, 0.8f));
            CoachLook.Write(p, WeekX + 54, 738, WeekW - 66, 22, $"plan ({st.rate} a day, rest days out)", 15, CoachLook.Soft).overflowMode = TextOverflowModes.Ellipsis;
        }
    }
}
