// Event Coach, i file (i calcoli sono in Coach.cs, la lettura dei file in CoachFiles.cs). Tutto nella cartella della mod (Store.Root):
//   coach.json               l'evento attivo (uno alla volta), l'archivio dei passati, e il riassunto dei log di
//                            CombatLogs\AutoSaved che la mod non ha registrato (degli ultimi 60 giorni). Le partite giocate
//                            senza la mod non contano (scelta di Leo), ma servono: il primo pezzo di una partita spezzata da
//                            "Restart this turn" la mod non lo registra (se non ha note), e senza di lui il pezzo dopo resterebbe solo
//   matches\<id>.json "game" il riassunto di ogni partita registrata dalla mod, letto dalla sua copia del log a fine partita
// Niente agganci nuovi al sim: si leggono solo le copie dei log della mod e la cartella dei log del sim (senza mai scriverci).
// I log da leggere (quelli di AutoSaved, le partite registrate prima del coach) si leggono in un altro thread: il sim non si
// ferma mai. Finché non hanno finito, il coach mostra quello che sa ma non fissa i numeri dei giorni.
// Lo stato si calcola quando serve (schermate, comando "coach"): mai a ogni fotogramma.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OPTCGTrainer
{
    static class CoachStore
    {
        static string FilePath => Path.Combine(Store.Root, "coach.json");
        // relativa alla cartella del sim, che è la cartella di lavoro (come in Match.RecoverLeftovers)
        internal static string AutoSaved => Path.GetFullPath(Path.Combine("CombatLogs", "AutoSaved"));
        static CoachFile data;
        static bool readOnly;

#pragma warning disable CS0649 // la scrive solo il telecomando, che nel pacchetto non c'è
        /// Solo per le prove (telecomando): un'ora del PC finta, per provare il cambio del giorno e i giorni saltati.
        internal static DateTime? FakeNow;
#pragma warning restore CS0649
        internal static DateTime Now => FakeNow ?? DateTime.Now;
#if DEVTOOLS
        /// Solo per le prove: partite finte, solo in memoria (mai salvate), per vedere le schermate piene.
        internal static readonly List<GameSummary> FakeGames = new List<GameSummary>();
#endif

        /// Sale quando cambia qualcosa che il coach mostra (partita finita, log letti, evento salvato): le schermate si rifanno.
        internal static int Version;

        internal static CoachFile Data
        {
            get
            {
                if (data != null) return data;
                data = CoachFiles.Load(FilePath, out var problem, out readOnly);
                if (problem != null) Plugin.Log.LogWarning("coach: " + problem);
                return data;
            }
        }

        static void Save()
        {
            if (readOnly) return;
            try { CoachFiles.Save(FilePath, Data); }
            catch (Exception e) { Plugin.Log.LogWarning("coach: coach.json non salvato: " + e.Message); }
        }

        // il tipo (ladder, queue, private, solo) e la coda li sa la mod, giocando: valgono più di quello che si legge nel log
        static void Fill(MatchFile m)
        {
            if (m.matchKind != null) m.game.kind = m.matchKind;
            if (m.matchFormat != null) m.game.format = m.matchFormat;
        }

        // ---- a fine partita ----

        /// Da Match.Register: la partita è appena finita e copiata. Il riassunto va nel suo file (lo salva Register).
        internal static void OnRegistered(MatchFile m, string source)
        {
            try
            {
                m.game = CoachFiles.Read(m.backup, TimeZoneInfo.Local, File.GetLastWriteTimeUtc(source));
                Fill(m);
                if (data != null && data.imported.Remove(m.id)) Save(); // letto da CombatLogs prima che la mod lo registrasse
                if (m.game.continuation && Data.active != null) ImportBefore(m.game); // senza un evento il coach non fa niente
                Version++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("coach: riassunto della partita non riuscito: " + e.Message); }
        }

        // un turno rifatto: il pezzo di prima (senza note la mod non l'ha registrato) si legge subito, prima che un
        // aggiornamento del sim cancelli CombatLogs. Pochi file piccoli: si fa qui
        static void ImportBefore(GameSummary piece)
        {
            if (!(Coach.Utc(piece.startedAt) is DateTime start) || !Directory.Exists(AutoSaved)) return;
            var known = new HashSet<string>(Store.AllMatches().Select(x => x.id));
            int added = 0;
            foreach (var name in CoachFiles.Before(Directory.GetFiles(AutoSaved, "*.log").Select(Path.GetFileName), start))
            {
                if (!GameLog.ParseName(name, out var id, out _) || known.Contains(id)) continue;
                if (Data.imported.TryGetValue(id, out var old) && old != null && old.v >= GameSummary.Current && old.logName == name) continue;
                var game = CoachFiles.Read(Path.Combine(AutoSaved, name), TimeZoneInfo.Local);
                game.imported = true;
                Data.imported[id] = CoachFiles.Slim(game);
                added++;
            }
            if (added > 0) { Save(); Plugin.Log.LogInfo($"coach: turno rifatto, {added} log di prima letti da CombatLogs\\AutoSaved"); }
        }

        // ---- la lettura in sottofondo ----

        class Done { internal MatchFile match; internal string id, name, error; internal GameSummary game; }

        static Task<List<Done>> job;
        static readonly Queue<Done> results = new Queue<Done>();
        static bool scanned;
        static float scannedAt;

        /// I log sono stati letti tutti (almeno una volta in questa sessione): i numeri dei giorni si possono fissare.
        internal static bool Ready => scanned && job == null && results.Count == 0;

        /// Fa partire la lettura dei log che mancano, in un altro thread: una volta per sessione, poi al più ogni 30 minuti.
        internal static void Kick(bool force = false)
        {
            if (job != null || results.Count > 0) return;
            if (scanned && !force && Time.realtimeSinceStartup - scannedAt < 1800) return;
            scannedAt = Time.realtimeSinceStartup;
            // qui, nel thread di Unity: solo copie (il thread di sotto non tocca niente della mod)
            var matches = Store.AllMatches();
            var known = new HashSet<string>(matches.Select(m => m.id));
            var redo = matches.Where(m => m.Finished && (m.game == null || m.game.v < GameSummary.Current))
                .Select(m => (match: m, backup: m.backup, logName: m.logName, finishedAt: m.finishedAt)).ToList();
            var playing = Match.Current != null && !Match.Current.Finished ? Match.Current.id : null;
            var imported = new Dictionary<string, GameSummary>(Data.imported);
            var keep = CoachFiles.KeepFrom(DateTime.UtcNow, Data.active?.start);
            var dir = AutoSaved;
            var tz = TimeZoneInfo.Local;
            job = Task.Run(() =>
            {
                var done = new List<Done>();
                foreach (var (m, backup, logName, finishedAt) in redo)
                {
                    try
                    {
                        // la copia della mod, o (se è stata cancellata) il log del sim
                        var source = CoachFiles.SourceFor(backup, dir, logName);
                        if (source == null) continue;
                        // la fine: il log del sim se c'è ancora (la sua data è l'ultima riga), se no la copia o quando la mod
                        // l'ha registrata, la prima delle due
                        var original = CoachFiles.SourceFor(null, dir, logName);
                        var end = File.GetLastWriteTimeUtc(original ?? source);
                        if (original == null && Coach.Utc(finishedAt) is DateTime finished && finished < end) end = finished;
                        done.Add(new Done { match = m, game = CoachFiles.Read(source, tz, end) });
                    }
                    catch (Exception e) { done.Add(new Done { name = logName, error = e.Message }); }
                }
                foreach (var path in CoachFiles.ToImport(dir, known, playing, imported, keep))
                {
                    try
                    {
                        var game = CoachFiles.Read(path, tz);
                        game.imported = true;
                        done.Add(new Done { id = game.id, game = CoachFiles.Slim(game) });
                    }
                    catch (Exception e) { done.Add(new Done { name = Path.GetFileName(path), error = e.Message }); }
                }
                return done;
            });
        }

        /// A ogni fotogramma (Plugin.Update): i risultati della lettura, 25 per volta (ognuno può salvare un file).
        internal static void Tick()
        {
            if (job != null && job.IsCompleted)
            {
                if (job.Status == TaskStatus.RanToCompletion) foreach (var d in job.Result) results.Enqueue(d);
                else Plugin.Log.LogWarning("coach: lettura dei log non riuscita: " + job.Exception?.GetBaseException().Message);
                job = null;
                scanned = true;
                if (results.Count == 0) Version++;
            }
            if (results.Count == 0) return;
            for (int n = 0; n < 25 && results.Count > 0; n++)
            {
                var d = results.Dequeue();
                if (d.error != null) Plugin.Log.LogWarning($"coach: log non letto ({d.name}): {d.error}");
                else if (d.match != null) { d.match.game = d.game; Fill(d.match); Store.SaveMatch(d.match); }
                else if (d.id != null) Data.imported[d.id] = d.game;
            }
            if (results.Count > 0) return;
            int pruned = CoachFiles.Prune(Data, CoachFiles.KeepFrom(DateTime.UtcNow, Data.active?.start));
            Save();
            Version++;
            Plugin.Log.LogInfo($"coach: log letti ({Data.imported.Count} da CombatLogs\\AutoSaved{(pruned > 0 ? $", {pruned} vecchi tolti" : "")})");
        }

        /// Solo per il telecomando: aspetta che la lettura finisca (le prove vogliono i numeri giusti subito).
        internal static void WaitReady(float seconds)
        {
            Kick();
            var clock = Stopwatch.StartNew();
            while (!Ready && clock.Elapsed.TotalSeconds < seconds)
            {
                if (job != null && !job.IsCompleted) Thread.Sleep(20);
                Tick();
            }
        }

        // ---- lo stato ----

        /// Tutti i log che il coach conosce: le partite della mod e quelli di CombatLogs\AutoSaved che la mod non ha registrato.
        /// Non legge niente: quello che manca lo legge Kick, in sottofondo.
        internal static List<GameSummary> Summaries()
        {
            var list = new List<GameSummary>();
            var known = new HashSet<string>();
            foreach (var m in Store.AllMatches())
            {
                known.Add(m.id);
                if (!m.Finished || m.game == null) continue;
                Fill(m);
                list.Add(m.game);
            }
            list.AddRange(Data.imported.Where(x => !known.Contains(x.Key) && x.Value != null).Select(x => x.Value));
#if DEVTOOLS
            list.AddRange(FakeGames);
#endif
            Kick();
            return list;
        }

        internal static List<CoachGame> Games() => Coach.Games(Summaries(), TimeZoneInfo.Local);

        // la partita aperta adesso (non ancora finita): da quando, nell'ora del PC
        static DateTime? PlayingSince()
        {
            var m = Match.Current;
            if (m == null || m.Finished || !GameLog.ParseName(m.logName, out _, out var utc)) return null;
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);
        }

        /// Lo stato del coach adesso; null senza un evento attivo (o se qualcosa va storto: il coach non deve mai rompere niente).
        /// La prima volta che si calcola un giorno, il suo numero si fissa (solo con i log letti tutti).
        internal static CoachState State()
        {
            try
            {
                // senza un evento attivo niente: né le partite, né la lettura in sottofondo, né un coach.json nuovo
                // (il Setup la fa partire quando serve)
                var ev = Data.active;
                if (ev == null) return null;
                var games = Games();
                var st = Coach.Compute(ev, games, Now);
                // il giorno dopo il torneo l'evento va da solo nell'archivio, con i suoi numeri (scelta di Leo)
                if (st.over) { Archive(st); return null; }
                var fix = Coach.TargetsToFix(st, Ready, PlayingSince());
                if (fix.Count > 0)
                {
                    foreach (var t in fix) ev.targets[t.Key] = t.Value;
                    Save();
                }
                return st;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("coach: stato non calcolato: " + e.Message);
                return null;
            }
        }

        /// Un evento nuovo al posto di quello attivo (quello di prima non va in archivio: per quello c'è Stop).
        /// Restituisce cosa non va (in inglese), o null se è stato salvato.
        internal static string Start(CoachEvent ev)
        {
            var problem = Coach.Problem(ev, Coach.DayOf(Now));
            if (problem != null) return problem;
            ev.id ??= Guid.NewGuid().ToString("N");
            ev.createdAt ??= Coach.Iso(DateTime.UtcNow);
            ev.restDays = (ev.restDays ?? new List<string>()).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
            ev.targets ??= new Dictionary<string, int>();
            Data.active = ev;
            Save();
            Version++;
            Plugin.Log.LogInfo($"coach: evento nuovo, {ev.goal} partite dal {ev.start} al {ev.readyBy}");
            return null;
        }

        /// Cambia l'evento attivo (dal Setup): stesso evento, con i numeri già fissati dei giorni passati; quello di oggi e
        /// dei giorni dopo si ricalcola con le regole nuove. Restituisce cosa non va (in inglese), o null se è stato salvato.
        internal static string Update(CoachEvent ev)
        {
            var problem = Coach.Problem(ev, Coach.DayOf(Now));
            if (problem != null) return problem;
            var old = Data.active;
            if (old == null) return Start(ev);
            ev.id = old.id;
            ev.createdAt = old.createdAt;
            ev.targets = Coach.KeepPastTargets(old, Coach.DayOf(Now)).targets;
            ev.restDays = (ev.restDays ?? new List<string>()).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
            Data.active = ev;
            Save();
            Version++;
            Plugin.Log.LogInfo($"coach: evento cambiato, {ev.goal} partite dal {ev.start} al {ev.readyBy}");
            return null;
        }

        /// Ferma l'evento attivo e lo mette nell'archivio con i suoi numeri.
        internal static ArchivedEvent Stop()
        {
            var st = State();
            return st == null ? null : Archive(st);
        }

        static ArchivedEvent Archive(CoachState st)
        {
            var done = new ArchivedEvent
            {
                ev = st.ev, games = st.total, wins = st.all.wins, losses = st.all.losses, level = st.level,
                bestStreak = st.bestStreak, archivedAt = Coach.Iso(DateTime.UtcNow),
            };
            Data.archive.Add(done);
            Data.active = null;
            Save();
            Version++;
            Plugin.Log.LogInfo(st.over ? "coach: evento passato, messo in archivio" : "coach: evento fermato e messo in archivio");
            return done;
        }
    }
}
