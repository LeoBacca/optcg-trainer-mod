// I file della mod, tutti in %LOCALAPPDATA%\OPTCGTrainer (fuori dalla cartella del sim: il suo aggiornamento cancella
// tutto quello che trova lì dentro, CombatLogs compresi).
//   account.json             il collegamento al sito: indirizzo, token del dispositivo, nickname
//   matches\<id>.json        una partita: il suo log, le note scritte giocando, se e dove è stata caricata
//   logs\<nome del log>      la copia di ogni log finito
// <id> è l'Id del ReplayEvidenceJournal del sim: resta uguale quando il file passa da .partial.log a .log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace OPTCGTrainer
{
    class AccountFile
    {
        public string site;
        public string token;
        public string nickname;
        public string linkedAt;
    }

    class NoteItem
    {
        public int line;       // quante righe aveva scritto il sim quando hai aperto la nota (righe da 1, come sul sito)
        public int turn;
        public string text;
        public string createdAt;
        public bool uploaded;
    }

    class MatchFile
    {
        public string id;
        public string logName;
        public string backup;       // percorso della copia in logs\
        public string finishedAt;
        public string replayId;     // sul sito, quando caricata
        public string site;         // il sito dove sta replayId (se la mod passa a un altro sito, lì va ricaricata)
        public string uploadError;  // il sito l'ha rifiutata (es. partita troppo corta): non si riprova
        public int maxTurn;         // il turno più alto visto giocando (una partita abbandonata al mulligan non si carica)
        public string matchKind;    // ladder (OPBounty) · queue (coda pubblica) · private (lobby con codice) · solo
        public string matchFormat;  // il nome della coda come lo vedi nel menu (Standard, OP18…), se c'è
        public int ladderMatch;     // il numero della partita OPBounty, per le partite di ladder
        public List<NoteItem> notes = new List<NoteItem>();

        [JsonIgnore] public bool Finished => !string.IsNullOrEmpty(backup);
        [JsonIgnore] public int PendingNotes => notes.Count(n => !n.uploaded);
    }

    static class Store
    {
        // con il telecomando delle prove acceso la mod usa una cartella tutta sua: le prove non toccano mai il collegamento
        // e le partite dell'installazione vera (si decide alla prima richiesta, dopo DevCommands.Init)
        static string root;
        internal static string Root => root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DevCommands.Enabled ? "OPTCGTrainer-prova" : "OPTCGTrainer");
        internal static string Logs => Dir("logs");
        static string Matches => Dir("matches");
        static string AccountPath => Path.Combine(Root, "account.json");

        static string Dir(string name)
        {
            var dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static T Load<T>(string path) where T : class
        {
            try
            {
                if (File.Exists(path)) return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("file illeggibile, lo ignoro: " + path + " (" + e.Message + ")");
            }
            return null;
        }

        // si scrive in un file accanto e poi lo si mette al posto giusto: se il sim si chiude a metà, il file vecchio resta intero
        static void Save(string path, object value)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(value, Formatting.Indented));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); }
            catch (Exception) { File.Copy(tmp, path, true); File.Delete(tmp); } // dischi o cartelle dove Replace non va
        }

        internal static AccountFile LoadAccount() => Load<AccountFile>(AccountPath);
        internal static void SaveAccount(AccountFile a) => Save(AccountPath, a);
        internal static void ForgetAccount() { if (File.Exists(AccountPath)) File.Delete(AccountPath); }

        // una partita è un oggetto solo per tutta la mod: lo schermo e il caricamento in sottofondo vedono le stesse modifiche
        static readonly Dictionary<string, MatchFile> cache = new Dictionary<string, MatchFile>();

        internal static MatchFile LoadMatch(string id)
        {
            if (!cache.TryGetValue(id, out var m))
            {
                m = Load<MatchFile>(MatchPath(id)) ?? new MatchFile { id = id };
                m.id = id;
                cache[id] = m;
            }
            return m;
        }

        internal static void SaveMatch(MatchFile m) => Save(MatchPath(m.id), m);
        static string MatchPath(string id) => Path.Combine(Matches, Safe(id) + ".json");

        /// Le partite finite che hanno ancora qualcosa da caricare su questo sito (il log o delle note), dalla più vecchia.
        internal static List<MatchFile> ToUpload(string site)
        {
            var list = new List<MatchFile>();
            foreach (var file in Directory.GetFiles(Matches, "*.json"))
            {
                var m = LoadMatch(Path.GetFileNameWithoutExtension(file));
                if (!m.Finished || m.uploadError != null) continue;
                // caricata su un altro sito (es. quello di prova): resta di quel sito, non si ricarica qui da sola
                if (m.replayId != null && m.site != site) continue;
                if (m.replayId == null || m.PendingNotes > 0) list.Add(m);
            }
            return list.OrderBy(m => m.finishedAt).ToList();
        }

        /// Le partite rimaste a metà (il sim si è chiuso o è crashato prima che la mod le registrasse).
        internal static List<MatchFile> Unfinished()
        {
            var list = new List<MatchFile>();
            foreach (var file in Directory.GetFiles(Matches, "*.json"))
            {
                var m = LoadMatch(Path.GetFileNameWithoutExtension(file));
                if (!m.Finished && m.logName != null) list.Add(m);
            }
            return list;
        }

        static string Safe(string id) => new string(id.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    }
}
