// Event Coach: coach.json e la lettura dei log sul disco. Niente Unity (si prova con tools/coach-test), niente stato:
// chi li usa (CoachStore.cs) decide quando e in che thread. I log del sim si leggono soltanto, in condivisione.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace OPTCGTrainer
{
    class ArchivedEvent
    {
        public CoachEvent ev;
        public int games, wins, losses, level, bestStreak; // com'era finito: "Regionals Milan: 142/150, streak 19"
        public string archivedAt;
    }

    class CoachFile
    {
        public CoachEvent active;
        public List<ArchivedEvent> archive = new List<ArchivedEvent>();
        public Dictionary<string, GameSummary> imported = new Dictionary<string, GameSummary>(); // per id del log
        public Dictionary<string, string> results = new Dictionary<string, string>();           // l'esito messo a mano: id del log → w o l
        public List<GameSummary> added = new List<GameSummary>();                                // le partite aggiunte a mano (la mod non le ha registrate)
    }

    static class CoachFiles
    {
        // senza rientri e senza i campi vuoti: il file si riscrive tutto a ogni salvataggio
        static readonly JsonSerializerSettings Compact = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None };
        internal const int ImportDays = 60;   // i log senza la mod servono solo per la durata media del Setup e per i turni rifatti

        /// coach.json. Se non c'è: vuoto. Se c'è ma non si legge (rovinato da un crash o da un calo di corrente), non si
        /// sovrascrive: si mette da parte come coach.json.bad-<ora> e si riparte vuoti. Se non si riesce nemmeno a spostarlo
        /// (bloccato), readOnly: in questa sessione non si scrive. problem dice cosa è successo (per il log della mod).
        internal static CoachFile Load(string path, out string problem, out bool readOnly)
        {
            problem = null;
            readOnly = false;
            if (!File.Exists(path)) return Fill(null);
            try
            {
                var data = JsonConvert.DeserializeObject<CoachFile>(File.ReadAllText(path));
                if (data == null) throw new InvalidDataException("file vuoto");
                return Fill(data);
            }
            catch (Exception e)
            {
                var bad = path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                try
                {
                    File.Move(path, bad);
                    problem = $"coach.json non si legge ({e.Message}): messo da parte come {Path.GetFileName(bad)}, si riparte da zero";
                }
                catch (Exception moving)
                {
                    readOnly = true;
                    problem = $"coach.json non si legge ({e.Message}) e non si sposta ({moving.Message}): in questa sessione non si scrive";
                }
                return Fill(null);
            }
        }

        static CoachFile Fill(CoachFile data)
        {
            data ??= new CoachFile();
            data.archive ??= new List<ArchivedEvent>();
            data.imported ??= new Dictionary<string, GameSummary>();
            data.results ??= new Dictionary<string, string>();
            data.added ??= new List<GameSummary>();
            data.added.RemoveAll(g => g == null || g.id == null);
            if (data.active != null)
            {
                data.active.restDays ??= new List<string>();
                data.active.targets ??= new Dictionary<string, int>();
            }
            return data;
        }

        /// Si scrive in un file accanto e poi lo si mette al posto giusto: se il sim si chiude a metà, il file vecchio resta intero.
        internal static void Save(string path, CoachFile data)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Compact));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception) { File.Copy(tmp, path, true); File.Delete(tmp); } // dischi o cartelle dove Replace non va
        }

        /// Da quando servono i log importati: gli ultimi 60 giorni, o dall'inizio dell'evento se è prima.
        internal static DateTime KeepFrom(DateTime nowUtc, string eventStart)
        {
            var keep = nowUtc.AddDays(-ImportDays);
            if (Coach.Day(eventStart) is DateTime start && start.AddDays(-1) < keep) keep = start.AddDays(-1);
            return keep;
        }

        /// Toglie i log importati più vecchi di keepFrom. Quanti ne ha tolti.
        internal static int Prune(CoachFile data, DateTime keepFromUtc)
        {
            var old = data.imported.Where(x => (Coach.Utc(x.Value?.startedAt) ?? DateTime.MinValue) < keepFromUtc).Select(x => x.Key).ToList();
            foreach (var id in old) data.imported.Remove(id);
            return old.Count;
        }

        /// Una partita aggiunta a mano: giocata adesso (conta per il giorno del coach di adesso), con l'esito e, se si sa,
        /// il leader dell'avversario e il mio. Niente log, niente turni, niente durata.
        internal static GameSummary Added(DateTime nowUtc, TimeZoneInfo tz, string result, string oppLeader, string myLeader = null) =>
            new GameSummary
            {
                v = GameSummary.Current, id = "hand-" + Guid.NewGuid().ToString("N"), added = true, ended = true,
                startedAt = Coach.Iso(nowUtc), endedAt = Coach.Iso(nowUtc), day = Coach.DayKey(nowUtc, tz),
                result = result == "w" || result == "l" ? result : "o", reason = "by hand",
                oppLeader = string.IsNullOrEmpty(oppLeader) ? null : oppLeader, myLeader = string.IsNullOrEmpty(myLeader) ? null : myLeader,
            };

        /// Di un log senza leader (il menu, un Quits!) basta ricordare che è già stato letto: id, nome, dimensione, inizio.
        internal static GameSummary Slim(GameSummary s) =>
            s.myLeader != null || s.oppLeader != null ? s
                : new GameSummary { v = s.v, id = s.id, logName = s.logName, startedAt = s.startedAt, size = s.size, imported = s.imported, result = null };

        /// Da dove leggere una partita della mod: la sua copia; se è stata cancellata, il log del sim (.log o .partial.log).
        internal static string SourceFor(string backup, string autoSaved, string logName)
        {
            if (!string.IsNullOrEmpty(backup) && File.Exists(backup)) return backup;
            if (string.IsNullOrEmpty(logName) || string.IsNullOrEmpty(autoSaved)) return null;
            foreach (var name in new[] { logName, logName.Replace(".partial.log", ".log"), logName.Replace(".log", ".partial.log") }.Distinct())
            {
                var path = Path.Combine(autoSaved, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// I log cominciati poco prima di questo (al più 4, nelle 3 ore prima): il pezzo prima di un turno rifatto, e
        /// quelli prima ancora. Si leggono subito, prima che un aggiornamento del sim cancelli CombatLogs.
        internal static List<string> Before(IEnumerable<string> fileNames, DateTime startUtc)
        {
            var window = TimeSpan.FromHours(3);
            return fileNames.Select(n => GameLog.ParseName(n, out _, out var t) ? (n, t) : (n, DateTime.MinValue))
                .Where(x => x.Item2 < startUtc && x.Item2 >= startUtc - window)
                .OrderByDescending(x => x.Item2).Take(4).Select(x => x.n).ToList();
        }

        /// I log di AutoSaved da leggere: non della mod, non la partita in corso, degli ultimi giorni, nuovi o cambiati.
        internal static List<string> ToImport(string dir, ICollection<string> known, string playing, IDictionary<string, GameSummary> imported, DateTime keepFromUtc)
        {
            var list = new List<string>();
            if (!Directory.Exists(dir)) return list;
            foreach (var path in Directory.GetFiles(dir, "*.log"))
            {
                var name = Path.GetFileName(path);
                if (!GameLog.ParseName(name, out var id, out var start) || known.Contains(id) || id == playing) continue;
                // la data nel nome è gratis: i log vecchi non si aprono nemmeno (se il nome è assurdo, decide la data del file)
                if (start < keepFromUtc && File.GetLastWriteTimeUtc(path) < keepFromUtc) continue;
                if (imported.TryGetValue(id, out var old) && old != null && old.logName == name && old.v >= GameSummary.Current
                    && old.size == new FileInfo(path).Length) continue;
                list.Add(path);
            }
            return list;
        }

        /// Il riassunto di un log sul disco, letto in condivisione (il sim può averlo ancora aperto): l'inizio dal nome
        /// (o dalla data del file, se quella del nome è assurda), la fine (data del file, se non la si sa meglio), il giorno
        /// del coach nel fuso di adesso, la dimensione.
        internal static GameSummary Read(string path, TimeZoneInfo tz, DateTime? endUtc = null)
        {
            var info = new FileInfo(path);
            GameSummary s;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                s = GameLog.Summarize(info.Name, Lines(reader));
            var end = endUtc ?? info.LastWriteTimeUtc;
            var start = Coach.Utc(s.startedAt) is DateTime fromName ? Coach.StartFor(fromName, end) : end;
            if (end < start) end = start;
            s.startedAt = Coach.Iso(start);
            s.endedAt = Coach.Iso(end);
            s.day = Coach.DayKey(start, tz);
            s.size = info.Length;
            return s;
        }

        static IEnumerable<string> Lines(StreamReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }
    }
}
