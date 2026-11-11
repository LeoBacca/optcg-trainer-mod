// Event Coach, i calcoli: solo C# puro (niente Unity, niente file), date e partite in ingresso, numeri in uscita.
// Si prova da solo con tools/coach-test (dotnet run). I file (coach.json, il riassunto delle partite, i log di
// CombatLogs\AutoSaved) li gestisce CoachStore.cs. Com'è fatto: docs/coach.md.
//
// Le regole (decisioni di Leo del 2026-11-09):
//   partita valida  giocata con la mod, dal giorno dello Start; ladder, queue, private (si sceglie; Solo v Self spento
//                   di default), nel formato dell'evento, almeno T3 come conta il sito; quelle con esito incerto contano,
//                   ma fuori dal winrate (come il sito)
//   giorno          finisce alle 4:00 di notte, ora del PC; una partita vale per il giorno in cui è cominciata
//   numero di oggi  (obiettivo − partite valide prima di oggi) ÷ giorni di gioco da oggi a "ready by" (senza i riposi),
//                   arrotondato in su; si fissa la prima volta che si calcola e resta quello per tutto il giorno
//   serie           giorni di fila con il numero del giorno raggiunto; i riposi (e i giorni senza niente da fare,
//                   obiettivo già raggiunto) non la rompono e non la allungano; oggi non la rompe finché non è finito.
//                   Un riposo si può mettere anche su un giorno passato: vale (scelta di Leo)
//   avviso          il numero del giorno è oltre il doppio di quello del primo giorno, e almeno 8
//   pass            20 livelli sulle partite valide: livello = partite × 20 ÷ obiettivo; titoli a 5, 10, 15; EVENT READY a 20
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OPTCGTrainer
{
    /// Il riassunto di un log del sim: una partita, o un pezzo (dopo "Restart this turn" il sim apre un log nuovo).
    /// Solo dati del gioco: i nomi dei giocatori non si leggono e non si salvano.
    class GameSummary
    {
        /// Sale quando il riassunto impara qualcosa di nuovo: quelli più vecchi si rileggono dal log (se c'è ancora).
        internal const int Current = 2;
        public int v;                // la versione del riassunto
        public string id;            // l'id del log: il guid nel nome del file (= ReplayEvidenceJournal.Id)
        public string logName;
        public string startedAt;     // UTC (ISO 8601), dal nome del file (o dalla data del file, se quella del nome è assurda)
        public string endedAt;       // UTC, l'ultima riga scritta (la data di modifica del log)
        public string day;           // il giorno del coach (yyyy-MM-dd), fissato quando si legge: se poi cambia il fuso del PC non si sposta
        public bool ended;           // c'è la riga RZ1|END: la partita è chiusa (un log dopo di lei non può essere un suo pezzo)
        public string result;        // w · l · o (incerto: disconnessione, uscita, log senza la riga di fine)
        public string reason;        // dalla riga END: win, loss, concede, opponent_concede; se no concede, quit, disconnect, unfinished
        public string myLeader, myLeaderName, oppLeader, oppLeaderName; // id della carta (OP13-001) e nome (Monkey D. Luffy)
        public int first;            // 1 = sono andato primo, 2 = secondo, 0 = non si sa
        public int turns;            // i turni come li conta il sito: quante volte comincia un turno (T1, T2…)
        public bool continuation;    // log aperto da "Restart this turn" (o dalle frecce): continua quello di prima
        public bool noNames;         // nomi vuoti nelle righe PLY: Solo v Self
        public int ladderMatch;      // il numero OPBounty della riga META (0 = non è ladder)
        public string kind;          // ladder · queue · private · solo · null (multiplayer: queue o private, non si sa)
        public string format;        // il nome della coda come nel menu (Standard, OP18…), se la mod l'ha visto
        public bool imported;        // letto da CombatLogs\AutoSaved: la mod non l'ha registrato giocando
        public long size;            // per i log importati: la dimensione letta (se cambia, si rilegge)
        public bool added;           // aggiunta a mano dal giocatore (la mod non l'ha registrata): non c'è nessun log
    }

    /// L'evento: quello attivo (uno alla volta) o uno dell'archivio. Le date sono giorni "yyyy-MM-dd" del coach.
    // i campi li scrivono Newtonsoft (coach.json), il Setup e il telecomando: senza telecomando il compilatore non li vede
#pragma warning disable CS0649
    class CoachEvent
    {
        public string id;
        public string name;
        public string start;         // il primo giorno del piano (di solito quello dello Start)
        public string eventDate;
        public string readyBy;
        public int goal;
        public List<string> restDays = new List<string>();
        public string format;        // null = tutti i formati
        public string leader;        // id della carta del leader dell'evento, null = nessuno (facoltativo)
        public bool ladder = true, queue = true, privateGames = true, solo = false;
        public Dictionary<string, int> targets = new Dictionary<string, int>(); // il numero fissato di ogni giorno già cominciato
        public string createdAt;

        internal CoachEvent Copy()
        {
            var c = (CoachEvent)MemberwiseClone();
            c.restDays = new List<string>(restDays ?? new List<string>());
            c.targets = new Dictionary<string, int>(targets ?? new Dictionary<string, int>());
            return c;
        }
    }
#pragma warning restore CS0649

    /// Una partita per il coach: un log, o più log di fila se la partita è stata spezzata da "Restart this turn".
    class CoachGame
    {
        public List<string> ids = new List<string>();
        public DateTime startUtc, endUtc, startLocal, day;
        public string result, reason, myLeader, myLeaderName, oppLeader, oppLeaderName, kind, format;
        public int first, turns, ladderMatch;
        public bool noNames, imported;
        public bool added;           // aggiunta a mano (niente log): conta sempre, nei giorni del piano
        public bool byHand;          // l'esito l'ha messo il giocatore (logResult: quello che dice il log)
        public string logResult;
        public double? minutes;      // null se non si sa (o se è assurda: zero, o più di 3 ore)
        public string why;           // null = conta per l'evento; se no il perché, in inglese come lo vedrà il giocatore
        public string Kind => kind ?? (ladderMatch > 0 ? "ladder" : noNames ? "solo" : null);
    }

    /// Vinte, perse e incerte: le incerte contano come giocate ma restano fuori dal winrate.
    class Record
    {
        public int games, wins, losses;
        public int Unknown => games - wins - losses;
        public int? Percent => wins + losses > 0 ? (int)Math.Round(100.0 * wins / (wins + losses), MidpointRounding.AwayFromZero) : (int?)null;
        internal void Add(CoachGame g)
        {
            games++;
            if (g.result == "w") wins++;
            else if (g.result == "l") losses++;
        }
    }

    class Matchup
    {
        public string leader, name;  // il leader dell'avversario
        public Record record = new Record();
        public bool Few => record.wins + record.losses < Coach.MinMatchupGames; // "–": meno di 3 partite con un esito
    }

    class CoachDay
    {
        public DateTime date;
        public Record record = new Record(); // le partite valide del giorno
        public int target;           // il numero del giorno (0 per i riposi, i giorni futuri e quelli senza niente da fare)
        public bool rest, isToday, future, readyBy, eventDay;
        public bool Met => target > 0 && record.games >= target;
    }

    class CoachWeek
    {
        public DateTime monday;
        public int games, plan;      // plan: i numeri dei giorni già cominciati + il numero di adesso per quelli che vengono
        public bool current, future;
    }

    /// Tutto quello che serve alle schermate, già calcolato.
    class CoachState
    {
        public CoachEvent ev;
        public DateTime now, today, dayEnds, start, eventDay, readyBy;
        public List<CoachGame> games, counted, todayGames;   // tutte (con il loro why), quelle valide, quelle di oggi (anche non valide)
        public int total, goal, toGo;
        public int todayCount, todayTarget, todayLeft;
        public int rate;             // il numero al giorno di adesso: quello di oggi, o del prossimo giorno di gioco (oggi riposo)
        public int firstTarget;      // il numero al giorno del primo giorno del piano: l'avviso scatta oltre il doppio
        public bool warning, restToday, beforeStart, over, behind, goalReached;
        public int daysToEvent, daysToReadyBy, playDays, restDays, playDaysLeft;
        public int streak, bestStreak;
        public int level, nextLevelAt, toNextLevel;
        public string title;
        public Record all = new Record(), goingFirst = new Record(), goingSecond = new Record(),
            todayRecord = new Record(), withLeader = new Record(), withLeaderToday = new Record();
        public string leaderName;
        public List<Matchup> matchups, matchupsWithLeader;
        public List<CoachDay> days = new List<CoachDay>();
        public List<CoachWeek> weeks = new List<CoachWeek>();
        public double totalMinutes;
        public double? avgMinutes;
        public Dictionary<string, int> newTargets = new Dictionary<string, int>(); // da fissare in coach.json
        public bool Badge(int days) => bestStreak >= days;
    }

    /// La lettura di un log del sim 1.45a, con le stesse regole del sito (src/core: stats.js, parser/read-steps.js).
    static class GameLog
    {
        // 2026-11-07T11.48.39.8988538-<id>.log (o .partial.log): ora UTC, come li salva il sim in CombatLogs\AutoSaved
        static readonly Regex FileName = new Regex(@"^(\d{4}-\d{2}-\d{2}T\d{2}\.\d{2}\.\d{2})(\.\d{1,7})?-([0-9A-Za-z]+)\.(partial\.)?log$");
        static readonly Regex Actor = new Regex(@"^\[(You|Opponent)\] (.*)$");
        static readonly Regex DrawCard = new Regex(@"^Draw \d+ Card$");
        static readonly Regex DrawDon = new Regex(@"^Draw \d+ Don$");
        static readonly Regex Link = new Regex("<link=\"([^\"]+)\">");

        internal static bool ParseName(string fileName, out string id, out DateTime startUtc)
        {
            id = null;
            startUtc = default;
            var m = FileName.Match(fileName ?? "");
            if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd'T'HH.mm.ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out startUtc)) return false;
            if (m.Groups[2].Success) startUtc = startUtc.AddTicks(long.Parse(m.Groups[2].Value.Substring(1).PadRight(7, '0'), CultureInfo.InvariantCulture));
            id = m.Groups[3].Value;
            return true;
        }

        internal static GameSummary Summarize(string fileName, IEnumerable<string> lines)
        {
            var s = new GameSummary { v = GameSummary.Current, logName = fileName, result = "o" };
            if (ParseName(fileName, out var id, out var start))
            {
                s.id = id;
                s.startedAt = Coach.Iso(start);
            }
            bool header = false, version = false, names = false, turnEnded = false, quit = false, cut = false;
            int active = 0, firstTurn = 0, chose = 0;
            string end = null, endReason = null, conceded = null;
            var leaderNames = new Dictionary<string, string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("RZ1|", StringComparison.Ordinal))
                {
                    var f = line.Split('|');
                    if (f.Length < 2) continue;
                    if (f[1] == "HDR") header = true;
                    else if (f[1] == "META" && f.Length > 3) int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out s.ladderMatch);
                    else if (f[1] == "PLY" && f.Length > 4)
                    {
                        // RZ1|PLY|n|nome|leader: 1 sono sempre io. Le righe si ripetono (le prime vuote): vale l'ultima piena
                        if (f[3].Replace("​", "").Trim() != "") names = true;
                        var leader = f[4].Trim();
                        if (leader != "" && f[2] == "1") s.myLeader = leader;
                        else if (leader != "" && f[2] == "2") s.oppLeader = leader;
                    }
                    else if (f[1] == "END" && f.Length > 4)
                    {
                        // RZ1|END|seq|1 o 2|motivo|hash: 1 = ha vinto chi ha scritto il log, cioè io
                        end = f[3] == "1" ? "w" : f[3] == "2" ? "l" : null;
                        endReason = f[4];
                    }
                    continue;
                }
                // una partita nuova comincia con la versione; il log di un turno rifatto no (LoadTurnState(newGame: false))
                if (line.StartsWith("Version is", StringComparison.Ordinal)) { version = true; continue; }
                var m = Actor.Match(line);
                if (!m.Success)
                {
                    if (line.Contains("Disconnected")) cut = true;
                    continue;
                }
                int p = m.Groups[1].Value == "You" ? 1 : 2;
                var body = m.Groups[2].Value.Trim();
                if (body.StartsWith("Leader is ", StringComparison.Ordinal))
                {
                    var link = Link.Match(body);
                    int bracket = body.IndexOf(" [", StringComparison.Ordinal);
                    var name = (bracket > 10 ? body.Substring(10, bracket - 10) : body.Substring(10)).Trim();
                    if (link.Success && !leaderNames.ContainsKey(link.Groups[1].Value)) leaderNames[link.Groups[1].Value] = name;
                }
                else if (body.StartsWith("Chose to go ", StringComparison.Ordinal))
                    chose = (p == 1) == body.EndsWith("First", StringComparison.Ordinal) ? 1 : 2;
                else if (DrawCard.IsMatch(body) || DrawDon.IsMatch(body) && active == 0)
                {
                    // come il sito: un turno comincia con "Draw n Card" dopo un End Turn o dell'altro giocatore,
                    // il primo di tutti (senza carta) con "Draw n Don"
                    if (!DrawCard.IsMatch(body) || turnEnded || p != active)
                    {
                        s.turns++;
                        if (firstTurn == 0) firstTurn = p;
                        active = p;
                        turnEnded = false;
                    }
                }
                else if (body == "End Turn") turnEnded = true;
                else if (body.StartsWith("Concedes", StringComparison.Ordinal)) conceded = p == 1 ? "l" : "w";
                else if (body.StartsWith("Quits", StringComparison.Ordinal)) quit = true;
                if (body.Contains("Disconnected")) cut = true;
            }
            s.continuation = header && !version;
            s.noNames = !names;
            s.first = firstTurn != 0 ? firstTurn : chose;
            if (s.myLeader != null && leaderNames.TryGetValue(s.myLeader, out var mine)) s.myLeaderName = mine;
            if (s.oppLeader != null && leaderNames.TryGetValue(s.oppLeader, out var theirs)) s.oppLeaderName = theirs;
            // l'esito: la riga END (sigillata dal sim) vale più di tutto; senza, come il sito: concede sì, disconnessione e uscita incerte
            s.ended = endReason != null;
            if (end != null) { s.result = end; s.reason = endReason; }
            else if (conceded != null) { s.result = conceded; s.reason = conceded == "l" ? "concede" : "opponent_concede"; }
            else s.reason = cut ? "disconnect" : quit ? "quit" : "unfinished";
            s.kind = s.ladderMatch > 0 ? "ladder" : s.noNames ? "solo" : null;
            return s;
        }
    }

    static class Coach
    {
        internal const int MinTurns = 3, Levels = 20, DayStartsAt = 4, MinMatchupGames = 3, MinWarning = 8;
        internal static readonly int[] StreakBadges = { 3, 7, 14, 21 };
        // tra la fine di un pezzo e il log del turno rifatto: in Solo 1–2 secondi; in multiplayer si aspetta che l'avversario
        // accetti (nel log non si scrive niente), e intanto si pensa: un'ora basta e avanza
        static readonly TimeSpan PieceGap = TimeSpan.FromMinutes(60);
        const double MaxMinutes = 180;
        const int MaxDays = 366, MaxGoal = 10000;

        internal static string Iso(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);
        internal static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        internal static DateTime? Utc(string iso) =>
            iso != null && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t : (DateTime?)null;

        internal static DateTime? Day(string key) =>
            key != null && DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        /// Il giorno del coach per un'ora del PC: finisce alle 4:00 di notte (una sessione 23:30–00:30 resta un giorno solo).
        internal static DateTime DayOf(DateTime local) => local.AddHours(-DayStartsAt).Date;

        static DateTime Monday(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

        /// Il giorno del coach di un'ora UTC, nel fuso dato (si salva nel riassunto quando la partita si legge).
        internal static string DayKey(DateTime utc, TimeZoneInfo tz) => Key(DayOf(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz)));

        /// L'inizio di una partita: l'ora nel nome del log, ma se è lontana più di 2 giorni dalla data del file (un PC con un
        /// calendario non gregoriano potrebbe scrivere l'anno 2569) quella del file.
        internal static DateTime StartFor(DateTime fromName, DateTime fileUtc) => Math.Abs((fileUtc - fromName).TotalDays) > 2 ? fileUtc : fromName;

        /// I numeri da fissare in coach.json: nessuno finché i log non sono stati letti tutti (sarebbero sbagliati per sempre);
        /// quello di oggi no se è aperta una partita cominciata ieri prima delle 4 (finita, conterà per ieri).
        internal static Dictionary<string, int> TargetsToFix(CoachState st, bool ready, DateTime? playingSinceLocal)
        {
            var fix = new Dictionary<string, int>();
            if (!ready || st == null) return fix;
            foreach (var t in st.newTargets) fix[t.Key] = t.Value;
            if (playingSinceLocal is DateTime since && DayOf(since) < st.today) fix.Remove(Key(st.today));
            return fix;
        }

        static bool Same(string a, string b) => a == null || b == null || a == b;

        /// Dai log alle partite: in ordine di inizio, il log di un turno rifatto si attacca alla partita del log appena prima,
        /// se quella non è chiusa (niente riga END) e ha gli stessi due leader. Un pezzo senza la partita di prima non diventa
        /// mai una partita a sé: sarebbe una partita "corta" che non c'è (o una partita contata due volte).
        /// results: gli esiti messi a mano (id del log → w o l), che valgono più di quello che dice il log. Le partite aggiunte
        /// a mano (added) non hanno un log: non entrano nella catena dei pezzi, si aggiungono in fondo così come sono.
        internal static List<CoachGame> Games(IEnumerable<GameSummary> logs, TimeZoneInfo tz, IDictionary<string, string> results = null)
        {
            var list = new List<CoachGame>();
            CoachGame last = null;
            bool lastEnded = true;
            var all = logs.Where(s => s != null && Utc(s.startedAt) != null).ToList();
            var sorted = all.Where(s => !s.added && (s.myLeader != null || s.oppLeader != null)).OrderBy(s => Utc(s.startedAt).Value);
            foreach (var s in sorted)
            {
                var start = Utc(s.startedAt).Value;
                var end = Utc(s.endedAt) ?? start;
                if (end < start) end = start;
                if (s.continuation)
                {
                    if (last == null || lastEnded || start < last.startUtc || start - last.endUtc > PieceGap
                        || !Same(last.myLeader, s.myLeader) || !Same(last.oppLeader, s.oppLeader))
                    {
                        last = null; // un pezzo senza la sua partita: niente, e nemmeno i pezzi che lo seguono
                        continue;
                    }
                    // il log nuovo ricomincia dall'inizio del turno rifatto: il suo primo turno è l'ultimo del pezzo di prima
                    last.ids.Add(s.id);
                    last.turns += Math.Max(0, s.turns - 1);
                    if (end > last.endUtc) last.endUtc = end;
                    last.result = s.result;
                    last.reason = s.reason;
                    last.myLeader ??= s.myLeader;
                    last.myLeaderName ??= s.myLeaderName;
                    last.oppLeader ??= s.oppLeader;
                    last.oppLeaderName ??= s.oppLeaderName;
                    last.kind ??= s.kind;
                    last.format ??= s.format;
                    if (last.first == 0) last.first = s.first;
                    if (last.ladderMatch == 0) last.ladderMatch = s.ladderMatch;
                    last.noNames &= s.noNames;
                    last.imported &= s.imported;
                    lastEnded = s.ended;
                    continue;
                }
                last = new CoachGame
                {
                    startUtc = start, endUtc = end, result = s.result, reason = s.reason,
                    myLeader = s.myLeader, myLeaderName = s.myLeaderName, oppLeader = s.oppLeader, oppLeaderName = s.oppLeaderName,
                    kind = s.kind, format = s.format, first = s.first, turns = s.turns, ladderMatch = s.ladderMatch,
                    noNames = s.noNames, imported = s.imported, day = Day(s.day) ?? default,
                };
                last.ids.Add(s.id);
                lastEnded = s.ended;
                list.Add(last);
            }
            if (results != null)
                foreach (var g in list)
                {
                    var by = g.ids.Where(id => id != null && results.ContainsKey(id)).Select(id => results[id]).FirstOrDefault(r => r == "w" || r == "l");
                    if (by == null) continue;
                    g.logResult = g.result;
                    g.result = by;
                    g.byHand = true;
                }
            foreach (var s in all.Where(s => s.added && s.id != null))
            {
                var at = Utc(s.startedAt).Value;
                var g = new CoachGame
                {
                    startUtc = at, endUtc = at, result = s.result == "w" || s.result == "l" ? s.result : "o", reason = "by hand",
                    myLeader = s.myLeader, myLeaderName = s.myLeaderName, oppLeader = s.oppLeader, oppLeaderName = s.oppLeaderName,
                    kind = s.kind, format = s.format, day = Day(s.day) ?? default, added = true, byHand = true,
                };
                g.ids.Add(s.id);
                list.Add(g);
            }
            list.Sort((a, b) => a.startUtc.CompareTo(b.startUtc));
            foreach (var g in list)
            {
                g.startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(g.startUtc, DateTimeKind.Utc), tz);
                if (g.day == default) g.day = DayOf(g.startLocal); // i riassunti vecchi, senza il giorno salvato
                var minutes = (g.endUtc - g.startUtc).TotalMinutes;
                g.minutes = minutes > 0 && minutes <= MaxMinutes ? minutes : (double?)null;
            }
            return list;
        }

        /// Le date dell'evento già lette, e i conti sui giorni.
        internal class Plan
        {
            internal readonly DateTime start, eventDay, readyBy, lastPlayDay; // lastPlayDay: "ready by", ma mai il giorno dell'evento
            internal readonly HashSet<DateTime> rest = new HashSet<DateTime>();
            readonly int goal;

            internal Plan(CoachEvent ev)
            {
                // date assurde (un coach.json scritto a mano) tenute tra il 2000 e il 2100: i conti sui giorni non escono dal calendario
                DateTime Sane(DateTime d) => d.Year < 2000 ? new DateTime(2000, 1, 1) : d.Year > 2100 ? new DateTime(2100, 12, 31) : d;
                eventDay = Sane(Day(ev.eventDate) ?? Day(ev.start) ?? DateTime.Today);
                start = Sane(Day(ev.start) ?? eventDay);
                if ((eventDay - start).Days > MaxDays) start = eventDay.AddDays(-MaxDays); // un coach.json rovinato non fa girare a vuoto
                readyBy = Sane(Day(ev.readyBy) ?? eventDay.AddDays(-2));
                lastPlayDay = readyBy < eventDay ? readyBy : eventDay.AddDays(-1);
                goal = Math.Max(1, Math.Min(ev.goal, MaxGoal)); // un coach.json scritto a mano non fa sforare i conti
                foreach (var key in ev.restDays ?? new List<string>())
                    if (Day(key) is DateTime d) rest.Add(d);
            }

            internal int PlayDays(DateTime from, DateTime to)
            {
                int n = 0;
                for (var d = from; d <= to; d = d.AddDays(1))
                    if (!rest.Contains(d)) n++;
                return n;
            }

            internal int RestDays(DateTime from, DateTime to) => rest.Count(d => d >= from && d <= to);

            /// Il numero del giorno d, sapendo quante partite valide c'erano prima. Dopo "ready by", se manca qualcosa,
            /// si continua fino al giorno prima dell'evento.
            internal int Target(DateTime d, int doneBefore)
            {
                if (d < start || d >= eventDay || rest.Contains(d)) return 0;
                int left = goal - doneBefore;
                if (left <= 0) return 0;
                var deadline = d <= lastPlayDay ? lastPlayDay : eventDay.AddDays(-1);
                int days = PlayDays(d, deadline);
                return days > 0 ? (left + days - 1) / days : 0;
            }

            /// Il numero del primo giorno, senza partite: quello che si vede nel Setup ("6 games a day").
            internal int FirstTarget
            {
                get
                {
                    int days = PlayDays(start, lastPlayDay);
                    return days > 0 ? (goal + days - 1) / days : goal;
                }
            }
        }

        /// L'evento con i numeri fissati solo dei giorni prima di oggi: quello di oggi e dei giorni dopo si ricalcolano con le
        /// regole nuove (salvando una modifica, e nell'anteprima del Setup, che se no mostrerebbe sempre il numero vecchio).
        internal static CoachEvent KeepPastTargets(CoachEvent ev, DateTime today)
        {
            var c = ev.Copy();
            var key = Key(today);
            c.targets = c.targets.Where(t => string.CompareOrdinal(t.Key, key) < 0).ToDictionary(t => t.Key, t => t.Value);
            return c;
        }

        /// "1 game", "3 games".
        internal static string N(int n, string word) => n.ToString(CultureInfo.InvariantCulture) + " " + word + (n == 1 ? "" : "s");

        /// Un nome lungo accorciato con "…": nelle righe piccole 40 caratteri non ci stanno.
        internal static string Short(string text, int max)
        {
            text = (text ?? "").Trim();
            return text.Length <= max ? text : text.Substring(0, Math.Max(1, max - 1)).TrimEnd() + "…";
        }

        /// Cosa non va in un evento (in inglese, per il Setup); null se va bene. Con today: anche una data dell'evento già passata.
        internal static string Problem(CoachEvent ev, DateTime? today = null)
        {
            if (ev == null) return "No event.";
            if (string.IsNullOrWhiteSpace(ev.name)) return "Give the event a name.";
            if (ev.goal < 1) return "The goal must be at least 1 game.";
            if (ev.goal > MaxGoal) return "The goal can be at most " + MaxGoal + " games.";
            if (Day(ev.start) == null || Day(ev.eventDate) == null || Day(ev.readyBy) == null) return "Set the dates.";
            if (new[] { ev.start, ev.eventDate, ev.readyBy }.Any(k => Day(k).Value.Year < 2000 || Day(k).Value.Year > 2100)) return "Set the dates.";
            if (!(ev.ladder || ev.queue || ev.privateGames || ev.solo)) return "Choose at least one kind of game.";
            var p = new Plan(ev);
            if (p.readyBy > p.eventDay) return "Ready by can't be after the event.";
            if (today is DateTime now && p.eventDay < now) return "The event date has passed: pick a day from today on.";
            if (p.readyBy < p.start) return "Ready by can't be before the first day.";
            if ((p.eventDay - p.start).Days >= MaxDays) return "The event is more than a year away.";
            if (p.PlayDays(p.start, p.lastPlayDay) == 0) return "There are no play days before the event.";
            return null;
        }

        /// Perché una partita non conta per l'evento; null se conta.
        internal static string Why(CoachEvent ev, Plan p, CoachGame g)
        {
            if (g.day < p.start) return "before the plan started";
            if (g.day > p.eventDay) return "after the event";
            // aggiunta a mano: la mod non andava, il giocatore dice che l'ha giocata. Conta e basta (tipo, formato e turni non si sanno)
            if (g.added) return null;
            // la mod non c'era (sim aperto senza la mod): non si sa il formato né se era queue o private
            if (g.imported) return "played without the mod";
            return Rules(ev, g);
        }

        /// Le regole dell'evento senza le date: tipo di partita, formato, turni.
        static string Rules(CoachEvent ev, CoachGame g)
        {
            switch (g.Kind)
            {
                case "solo": if (!ev.solo) return "Solo v Self"; break;
                case "ladder": if (!ev.ladder) return "ladder"; break;
                case "queue": if (!ev.queue) return "queue"; break;
                case "private": if (!ev.privateGames) return "private"; break;
                default: if (!ev.queue && !ev.privateGames) return "not ladder"; break; // multiplayer, non si sa se queue o private
            }
            // il formato lo sa solo la mod, giocando: lobby private e Solo v Self non ce l'hanno, e contano
            if (!string.IsNullOrEmpty(ev.format) && !string.IsNullOrEmpty(g.format) && !string.Equals(ev.format.Trim(), g.format.Trim(), StringComparison.OrdinalIgnoreCase))
                return "other format (" + g.format + ")";
            if (g.turns < MinTurns) return "too short (turn " + g.turns + ")";
            return null;
        }

        internal static int Level(int games, int goal) => goal <= 0 ? 0 : Math.Min(Levels, Math.Max(0, games) * Levels / goal);
        internal static int GamesForLevel(int level, int goal) => (level * goal + Levels - 1) / Levels;
        internal static string Title(int level) => level >= 20 ? "EVENT READY" : level >= 15 ? "gold" : level >= 10 ? "silver" : level >= 5 ? "bronze" : null;

        static bool Mine(CoachEvent ev, CoachGame g) => !string.IsNullOrEmpty(ev.leader) && string.Equals(g.myLeader, ev.leader, StringComparison.OrdinalIgnoreCase);

        static List<Matchup> Matchups(IEnumerable<CoachGame> games) =>
            games.GroupBy(g => g.oppLeader ?? "?").Select(x =>
            {
                var m = new Matchup { leader = x.Key, name = x.Select(g => g.oppLeaderName).FirstOrDefault(n => n != null) };
                foreach (var g in x) m.record.Add(g);
                return m;
            }).OrderByDescending(m => m.record.games).ThenBy(m => m.name ?? m.leader).ToList();

        /// Tutto il coach a quest'ora. Pura: i numeri dei giorni da fissare escono in newTargets (li salva CoachStore).
        internal static CoachState Compute(CoachEvent ev, List<CoachGame> games, DateTime nowLocal)
        {
            var p = new Plan(ev);
            var fixedTargets = ev.targets ?? new Dictionary<string, int>();
            var st = new CoachState
            {
                ev = ev, now = nowLocal, today = DayOf(nowLocal), games = games, goal = Math.Max(1, Math.Min(ev.goal, MaxGoal)),
                start = p.start, eventDay = p.eventDay, readyBy = p.readyBy, firstTarget = p.FirstTarget,
            };
            st.dayEnds = st.today.AddDays(1).AddHours(DayStartsAt);
            foreach (var g in games) g.why = Why(ev, p, g);
            st.counted = games.Where(g => g.why == null).OrderBy(g => g.startUtc).ToList();
            st.total = st.counted.Count;
            st.toGo = Math.Max(0, st.goal - st.total);
            st.todayGames = games.Where(g => g.day == st.today).OrderBy(g => g.startUtc).ToList();

            // giorno per giorno, dal primo del piano all'evento: partite, numero (fissato o da fissare)
            var byDay = st.counted.GroupBy(g => g.day).ToDictionary(x => x.Key, x => x.ToList());
            int before = 0;
            for (var d = p.start; d <= p.eventDay; d = d.AddDays(1))
            {
                var day = new CoachDay { date = d, rest = p.rest.Contains(d), isToday = d == st.today, future = d > st.today, readyBy = d == p.readyBy, eventDay = d == p.eventDay };
                if (byDay.TryGetValue(d, out var list)) foreach (var g in list) day.record.Add(g);
                if (!day.future && !day.rest) // un riposo azzera il numero del giorno, anche se è stato messo dopo
                {
                    var key = Key(d);
                    if (fixedTargets.TryGetValue(key, out var fixedTarget)) day.target = fixedTarget;
                    else st.newTargets[key] = day.target = p.Target(d, before);
                }
                before += day.record.games;
                st.days.Add(day);
            }

            // oggi
            var today = st.days.FirstOrDefault(d => d.isToday);
            st.beforeStart = st.today < p.start;
            st.over = st.today > p.eventDay;
            if (today != null)
            {
                st.todayCount = today.record.games;
                st.todayTarget = today.target;
                st.restToday = today.rest;
            }
            st.todayLeft = Math.Max(0, st.todayTarget - st.todayCount);
            st.goalReached = st.toGo == 0;
            st.behind = st.today > p.readyBy && !st.goalReached;
            st.daysToEvent = (p.eventDay - st.today).Days;
            st.daysToReadyBy = (p.readyBy - st.today).Days;
            st.playDays = p.PlayDays(p.start, p.lastPlayDay);
            st.restDays = p.RestDays(p.start, p.lastPlayDay);
            // in ritardo dopo "ready by" si gioca fino al giorno prima dell'evento: i giorni che restano e il piano contano fin lì
            var lastDay = st.behind ? p.eventDay.AddDays(-1) : p.lastPlayDay;
            st.playDaysLeft = p.PlayDays(st.today > p.start ? st.today : p.start, lastDay);

            // il numero di adesso: quello di oggi; se oggi è riposo (o il piano non è cominciato), quello del prossimo giorno di gioco
            st.rate = st.todayTarget;
            if (st.rate == 0 && !st.goalReached && !st.over)
            {
                var next = st.today < p.start ? p.start : st.today.AddDays(1);
                while (next < p.eventDay && p.rest.Contains(next)) next = next.AddDays(1);
                st.rate = p.Target(next, st.counted.Count(g => g.day < next));
            }
            // avviso: il numero è diventato più del doppio di quello del primo giorno, e almeno 8 (abbassa l'obiettivo o sposta "ready by")
            st.warning = st.firstTarget > 0 && st.rate > 2 * st.firstTarget && st.rate >= MinWarning;

            // la serie
            int run = 0;
            foreach (var day in st.days)
            {
                if (day.future) break;
                if (day.isToday) { if (day.Met) run++; }      // oggi non la rompe finché non è finito
                else if (day.rest || day.target == 0) { }       // riposo o niente da fare: non la rompe e non la allunga
                else if (day.Met) run++;
                else run = 0;
                if (run > st.bestStreak) st.bestStreak = run;
            }
            st.streak = run;

            // il pass
            st.level = Level(st.total, st.goal);
            st.title = Title(st.level);
            st.nextLevelAt = st.level < Levels ? GamesForLevel(st.level + 1, st.goal) : st.goal;
            st.toNextLevel = Math.Max(0, st.nextLevelAt - st.total);

            // le statistiche
            int timed = 0;
            foreach (var g in st.counted)
            {
                st.all.Add(g);
                if (g.first == 1) st.goingFirst.Add(g);
                else if (g.first == 2) st.goingSecond.Add(g);
                bool mine = Mine(ev, g);
                if (mine) st.withLeader.Add(g);
                if (g.day == st.today)
                {
                    st.todayRecord.Add(g);
                    if (mine) st.withLeaderToday.Add(g);
                }
                if (g.minutes != null) { st.totalMinutes += g.minutes.Value; timed++; }
            }
            st.avgMinutes = timed > 0 ? st.totalMinutes / timed : (double?)null;
            st.leaderName = games.Where(g => Mine(ev, g)).Select(g => g.myLeaderName).FirstOrDefault(n => n != null);
            st.matchups = Matchups(st.counted);
            st.matchupsWithLeader = string.IsNullOrEmpty(ev.leader) ? st.matchups : Matchups(st.counted.Where(g => Mine(ev, g)));

            // le settimane (da lunedì), con la linea del piano: per i giorni già cominciati il loro numero,
            // per quelli che vengono il numero di adesso (riposi esclusi)
            var dayInfo = st.days.ToDictionary(d => d.date);
            for (var w = Monday(p.start); w <= p.eventDay; w = w.AddDays(7))
            {
                var week = new CoachWeek { monday = w, current = st.today >= w && st.today < w.AddDays(7), future = w > st.today };
                for (var d = w; d < w.AddDays(7); d = d.AddDays(1))
                {
                    if (!dayInfo.TryGetValue(d, out var day)) continue;
                    week.games += day.record.games;
                    if (!day.future) week.plan += day.target;
                    else if (d <= lastDay && !day.rest) week.plan += st.rate;
                }
                st.weeks.Add(week);
            }
            return st;
        }

        /// Per il Setup, prima dello Start: giorni di gioco, riposi e partite al giorno dell'evento com'è impostato.
        internal static (int playDays, int restDays, int perDay) Preview(CoachEvent ev)
        {
            var p = new Plan(ev);
            return (p.PlayDays(p.start, p.lastPlayDay), p.RestDays(p.start, p.lastPlayDay), p.FirstTarget);
        }

        /// La durata media delle partite che conterebbero con queste regole (le date non contano): "your games last 21 min".
        internal static double? AverageMinutes(CoachEvent ev, IEnumerable<CoachGame> games)
        {
            var known = games.Where(g => g.minutes != null && Rules(ev, g) == null).Select(g => g.minutes.Value).ToList();
            return known.Count > 0 ? known.Average() : (double?)null;
        }
    }
}
