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
            bool counts = g.why == null, open = counts && rowEdit == g.ids[0], unknown = counts && g.result == "o";
            var chip = !counts ? CoachLook.Unknown : g.result == "w" ? CoachLook.Win : g.result == "l" ? CoachLook.Loss : CoachLook.Unknown;
            CoachLook.Box(p, 208, y + 4, 28, 28, chip, 4);
            CoachLook.Write(p, 208, y + 4, 28, 28, counts && g.result == "w" ? "W" : counts && g.result == "l" ? "L" : unknown ? "?" : "–", 16, counts ? Color.white : CoachLook.Soft, true, TextAlignmentOptions.Center);
            var color = counts ? CoachLook.Text : CoachLook.Dim;
            var vs = CoachLook.Write(p, 250, y + 6, 272, 26, "vs " + (g.oppLeader == null ? "unknown leader" : LeaderName(g.oppLeader, g.oppLeaderName)), 19, color);
            vs.overflowMode = TextOverflowModes.Ellipsis;
            void Set(string result) { CoachStore.SetResult(g, result); rowEdit = null; }
            if (open || unknown)
            {
                float x = 530;
                if (unknown && !open) { CoachLook.Write(p, x, y + 8, 84, 24, "no result:", 17, CoachLook.Gold); x += 88; }
                Pill(p, x, y + 4, 66, 28, "WON", g.result == "w" ? CoachLook.Win : CoachLook.Plan, Color.white, () => Set("w"));
                Pill(p, x + 70, y + 4, 66, 28, "LOST", g.result == "l" ? CoachLook.Loss : CoachLook.Plan, Color.white, () => Set("l"));
                // di nuovo come dice il log (se l'esito era stato messo a mano), o via la partita aggiunta a mano
                if (open && g.added) Pill(p, x + 140, y + 4, 84, 28, "REMOVE", CoachLook.Plan, CoachLook.Warn, () => { CoachStore.RemoveAdded(g); rowEdit = null; });
                else if (open && g.byHand) Pill(p, x + 140, y + 4, 84, 28, "UNDO", CoachLook.Plan, CoachLook.Soft, () => Set(null));
            }
            else
            {
                string det = !counts ? g.why + ", not counted"
                    : g.added ? "added by hand"
                    : $"{g.turns} turns · {(g.minutes != null ? (g.minutes.Value < 0.5 ? "<1" : Math.Round(g.minutes.Value).ToString(CultureInfo.InvariantCulture)) + " min · " : "")}{(g.first == 1 ? "went first" : g.first == 2 ? "went second" : "")}".TrimEnd(' ', '·')
                        + (g.byHand ? " · by hand" : "");
                CoachLook.Write(p, 530, y + 8, 330, 24, det, 17, counts ? CoachLook.Label : CoachLook.Dim).overflowMode = TextOverflowModes.Ellipsis;
            }
            CoachLook.Write(p, 836, y + 9, 86, 22, g.startLocal.ToString(withDay ? "d MMM" : "HH:mm", CultureInfo.InvariantCulture), 16, CoachLook.Dim, false, TextAlignmentOptions.TopRight);
            if (open) Pill(p, 932, y + 4, 48, 28, "close", CoachLook.Card, CoachLook.Label, () => rowEdit = null);
            else if (counts && !unknown) Pill(p, 932, y + 4, 48, 28, "edit", CoachLook.Card, CoachLook.Label, () => rowEdit = g.ids[0]);
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

        static bool allLeaders;
        static GameObject tip;

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
                Card(p, x, 180, 376, 110);
                CoachLook.Caps(p, x + 24, 194, 330, kpis[i].Item1, 14);
                CoachLook.Write(p, x + 24, 210, 330, 56, kpis[i].Item2, 46, Color.white, true);
                CoachLook.Write(p, x + 24, 264, 340, 22, kpis[i].Item3, 17, CoachLook.Label);
            }
            Heatmap(p, st);
            Matchups(p, st);
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
            CoachLook.Write(p, x, 343, 330, 18, $"{from.ToString("d MMM", CultureInfo.InvariantCulture)} – {to.ToString("d MMM", CultureInfo.InvariantCulture)} · {count} of {st.weeks.Count} weeks",
                13, CoachLook.Dim);
        }

        static void Heatmap(RectTransform p, CoachState st)
        {
            Card(p, 184, 306, 560, 460);
            CoachLook.Caps(p, 208, 324, 220, "Games per day");
            // la scala
            CoachLook.Write(p, 404, 323, 32, 22, "less", 14, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            string[] bins = { "0", "1–2", "3–4", "5–6", "7+" };
            for (int i = 0; i < 5; i++)
            {
                CoachLook.Box(p, 440 + i * 38, 322, 34, 22, CoachLook.Bins[i], 3);
                CoachLook.Write(p, 440 + i * 38, 322, 34, 22, bins[i], 13, i == 0 ? CoachLook.Soft : i == 1 ? Color.white : CoachLook.Dark, true, TextAlignmentOptions.Center);
            }
            CoachLook.Write(p, 634, 323, 50, 22, "more", 14, CoachLook.Label);
            var (first, count) = WeekWindow(st);
            WeekRange(p, 208, st, first, count);

            // una colonna per settimana (fino a 10), una riga per giorno; la legenda sotto
            const float X0 = 256, Y0 = 386, S = 44, C = 40;
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
                float x = 208 + (i % 3) * 172, y = 700 + (i / 3) * 30;
                items[i].draw(x, y + 2);
                CoachLook.Write(p, x + 28, y, 140, 24, items[i].text, 15, CoachLook.Soft, false, TextAlignmentOptions.MidlineLeft);
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

        static void Matchups(RectTransform p, CoachState st)
        {
            Card(p, 760, 306, 590, 460);
            CoachLook.Caps(p, 784, 324, 200, "Matchups");
            bool mine = st.ev.leader != null && !allLeaders;
            var list = mine ? st.matchupsWithLeader : st.matchups;
            int games = list.Sum(m => m.record.games);
            CoachLook.Write(p, 960, 323, 130, 22, games == 1 ? "1 game" : games + " games", 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);
            if (st.ev.leader != null)
            {
                var toggle = CoachLook.Button(p, host.go_Settings, 1104, 314, 222, 38, mine ? "Your leader: " + Coach.Short(LeaderName(st.ev.leader, st.leaderName), 18) : "All your leaders", false,
                    () => { allLeaders = !allLeaders; redraw = true; }, 17);
                // un leader dal nome lungo ("Green-Purple Doflamingo"): la scritta si rimpicciolisce invece di uscire dal pulsante
                var label = toggle.GetComponentInChildren<TMP_Text>(true);
                if (label != null) { label.enableAutoSizing = true; label.fontSizeMin = 11; label.fontSizeMax = 17; label.margin = new Vector4(8, 0, 8, 0); }
            }
            else CoachLook.Write(p, 1104, 323, 222, 22, "all your leaders", 15, CoachLook.Label, false, TextAlignmentOptions.TopRight);

            CoachLook.Write(p, 784, 362, 214, 20, "<cspace=0.06em>VS LEADER</cspace>", 13, CoachLook.Dim, true);
            CoachLook.Write(p, 978, 362, 70, 20, "<cspace=0.06em>GAMES</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, 1048, 362, 74, 20, "<cspace=0.06em>W–L</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
            CoachLook.Write(p, 1180, 362, 60, 20, "50%", 13, CoachLook.Dim, false, TextAlignmentOptions.Top);
            CoachLook.Write(p, 1260, 362, 66, 20, "<cspace=0.06em>WIN</cspace>", 13, CoachLook.Dim, true, TextAlignmentOptions.TopRight);
            if (list.Count == 0) CoachLook.Write(p, 784, 400, 542, 26, "No games yet.", 18, CoachLook.Dim);

            // al più 8 righe: le ultime diventano "Others (N leaders)"
            var rows = list.Take(list.Count > 8 ? 7 : 8).Select(m => (LeaderName(m.leader, m.name), m.record, m.Few)).ToList();
            if (list.Count > 8)
            {
                var rest = new Record();
                foreach (var m in list.Skip(7)) { rest.games += m.record.games; rest.wins += m.record.wins; rest.losses += m.record.losses; }
                rows.Add(($"Others ({list.Count - 7} leaders)", rest, rest.wins + rest.losses < Coach.MinMatchupGames));
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var (name, r, few) = rows[i];
                float y = 392 + i * 40;
                var c = few ? CoachLook.Dim : CoachLook.Text;
                CoachLook.Write(p, 784, y + 7, 214, 26, name, 18, c);
                CoachLook.Write(p, 978, y + 8, 70, 24, r.games.ToString(CultureInfo.InvariantCulture), 18, few ? CoachLook.Dim : CoachLook.Soft, false, TextAlignmentOptions.TopRight);
                CoachLook.Write(p, 1048, y + 8, 74, 24, $"{r.wins}–{r.losses}", 18, few ? CoachLook.Dim : CoachLook.Soft, false, TextAlignmentOptions.TopRight);
                CoachLook.Box(p, 1210, y + 6, 1, 26, CoachLook.Outline);
                if (!few && r.Percent is int pct)
                {
                    float px = Mathf.Max(Mathf.Min(Mathf.Abs(pct - 50), 25) * 70f / 25f, 2);
                    var color = pct > 50 ? CoachLook.Win : pct < 50 ? CoachLook.Loss : CoachLook.Even;
                    CoachLook.Box(p, pct >= 50 ? 1211 : 1210 - px, y + 12, px, 14, color, 3);
                }
                CoachLook.Write(p, 1260, y + 8, 66, 24, few || r.Percent == null ? "–" : r.Percent + "%", 18, few ? CoachLook.Dim : Color.white, !few, TextAlignmentOptions.TopRight);
                if (i < rows.Count - 1) CoachLook.Box(p, 784, y + 38, 542, 1, CoachLook.Row);
            }
            CoachLook.Box(p, 784, 728, 22, 12, CoachLook.Win, 3);
            CoachLook.Write(p, 814, 723, 120, 22, "you win more", 15, CoachLook.Soft);
            CoachLook.Box(p, 932, 728, 22, 12, CoachLook.Loss, 3);
            CoachLook.Write(p, 962, 723, 120, 22, "you lose more", 15, CoachLook.Soft);
            CoachLook.Write(p, 1084, 723, 250, 22, "– = fewer than 3 games", 15, CoachLook.Label);
        }

        static void Weeks(RectTransform p, CoachState st)
        {
            Card(p, 1366, 306, 370, 460);
            CoachLook.Caps(p, 1390, 324, 300, "Games per week");
            var (first, count) = WeekWindow(st);
            WeekRange(p, 1390, st, first, count);
            var shown = st.weeks.Skip(first).Take(count).ToList();
            if (shown.Count == 0) return;
            const float B = 690, Left = 1392, Width = 318;
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
            CoachLook.Box(p, 1390, 749, 22, 2, new Color(1, 1, 1, 0.8f));
            CoachLook.Write(p, 1420, 738, 300, 22, $"plan ({st.rate} a day, rest days out)", 15, CoachLook.Soft);
        }
    }
}
