// Il collegamento con il sito OPTCG Trainer (contratto in docs/api-sito.md).
//  - Collegare: il sim chiede un codice, apre il sito sulla pagina di conferma, aspetta che tu dica "Link";
//    il sito gli consegna un token che sa fare solo tre cose (chi sono, carica un log, scrivi una nota sui tuoi replay).
//  - Caricare: a fine partita il log e le note vanno sul tuo account; si riprova da sola all'avvio se il sito non rispondeva.
// La mod non vede mai cookie né password. Il token sta in %LOCALAPPDATA%\OPTCGTrainer\account.json e va solo al sito
// delle impostazioni (Site.Url), mai dietro a un rinvio.
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace OPTCGTrainer
{
    static class Account
    {
        static AccountFile account;
        static bool uploading, again;
        static string linkUrl;

        // lo stato del collegamento, per la UI
        internal static string LinkCode;      // il codice da confermare sul sito, mentre si collega
        internal static bool Linking;
        internal static string LastError;     // l'ultimo problema con il sito, in parole semplici (in inglese, come il sim)

        internal static string Site => Plugin.SiteUrl.TrimEnd('/');
        internal static bool Linked => account != null && account.token != null && account.site == Site;
        internal static string Nickname => Linked ? account.nickname : null;

        static readonly Regex UserCode = new Regex("^[A-Z]{4}-[A-Z]{4}$");

        internal static void Init()
        {
            account = Store.LoadAccount();
            if (account != null && account.site != Site)
                Plugin.Log.LogInfo($"il token è per {account.site}, la mod adesso usa {Site}: da ricollegare");
            Kick(); // carica quello che era rimasto indietro l'ultima volta
        }

        internal static void OpenReplay(string replayId) => Open(Site + "/?r=" + Uri.EscapeDataString(replayId));

        /// Apre il sito nel browser. Con il telecomando delle prove acceso scrive solo l'indirizzo nel log (niente schede aperte da sole).
        internal static void Open(string url)
        {
            if (DevCommands.Enabled) { Plugin.Log.LogInfo("[prova] il browser aprirebbe: " + url); return; }
            Application.OpenURL(url);
        }

        // ---- collegare ----

        /// Fa partire il collegamento; se è già partito, riapre la pagina del sito (magari hai chiuso la scheda).
        internal static void StartLink()
        {
            if (Linking) { if (linkUrl != null) Open(linkUrl); return; }
            Plugin.Instance.StartCoroutine(Link());
        }

        static IEnumerator Link()
        {
            Linking = true;
            LinkCode = null;
            linkUrl = null;
            LastError = null;
            try
            {
                var start = new Request("POST", "/api/device/start", Json(new { name = "OPTCGSim on " + Environment.MachineName }));
                yield return start.Send();
                var code = start.Ok ? (string)start.Body["userCode"] : null;
                if (code == null || !UserCode.IsMatch(code))
                {
                    LastError = start.Ok ? "The website sent a strange answer. Try again later." : Unreachable(start);
                    yield break;
                }
                var deviceCode = (string)start.Body["deviceCode"];
                LinkCode = code;
                // l'indirizzo lo fa la mod: dal server prende solo il codice (OpenURL aprirebbe qualsiasi cosa, anche un programma)
                linkUrl = Site + "/?link=" + Uri.EscapeDataString(code);
                float interval = Mathf.Clamp((float?)start.Body["interval"] ?? 2, 1, 10);
                float until = Time.realtimeSinceStartup + Mathf.Clamp((float?)start.Body["expiresIn"] ?? 600, 60, 900);
                Open(linkUrl);
                Plugin.Log.LogInfo("collegamento: codice " + LinkCode + ", aspetto la conferma sul sito");

                while (Time.realtimeSinceStartup < until)
                {
                    yield return new WaitForSecondsRealtime(interval);
                    var poll = new Request("POST", "/api/device/poll", Json(new { deviceCode }));
                    yield return poll.Send();
                    if (poll.Status == 200 && (string)poll.Body["status"] == "pending") continue;
                    if (poll.Status == 200 && (string)poll.Body["status"] == "linked" && (string)poll.Body["token"] != null)
                    {
                        account = new AccountFile
                        {
                            site = Site,
                            token = (string)poll.Body["token"],
                            nickname = (string)poll.Body["user"]?["nickname"],
                            linkedAt = DateTime.UtcNow.ToString("o"),
                        };
                        Store.SaveAccount(account);
                        Plugin.Log.LogInfo("collegato come " + account.nickname);
                        Kick();
                        yield break;
                    }
                    if (Temporary(poll.Status)) continue; // niente rete per un attimo: si riprova
                    LastError = poll.Status == 404 ? "The code expired. Try again." : "Linking didn't work. Try again.";
                    Plugin.Log.LogWarning($"collegamento: {poll.Status} {poll.Problem}");
                    yield break;
                }
                LastError = "The code expired. Try again.";
            }
            finally
            {
                // qualunque cosa succeda (anche un errore), si deve poter riprovare
                Linking = false;
                LinkCode = null;
                linkUrl = null;
            }
        }

        // ---- caricare ----

        /// Carica in sottofondo tutto quello che manca (log e note). Se sta già caricando, ripassa alla fine.
        internal static void Kick()
        {
            if (!Linked) return;
            if (uploading) { again = true; return; }
            Plugin.Instance.StartCoroutine(UploadAll());
        }

        static IEnumerator UploadAll()
        {
            uploading = true;
            try
            {
                do
                {
                    again = false;
                    LastError = null;
                    foreach (var m in Store.ToUpload(Site))
                    {
                        if (!Linked) yield break;
                        if (later.Contains(m.id)) continue;
                        if (m.replayId == null) yield return UploadLog(m);
                        if (m.replayId != null && m.PendingNotes > 0) yield return UploadNotes(m);
                        if (LastError != null) yield break; // il sito non risponde: si riprova alla prossima partita o al prossimo avvio
                    }
                } while (again);
            }
            finally { uploading = false; }
        }

        // le partite a cui il sito ha risposto in un modo strano (un 403 o un 404 di un proxy, una manutenzione): non si
        // segnano come rifiutate per sempre e non fermano le altre; si riprovano al prossimo avvio del sim
        static readonly System.Collections.Generic.HashSet<string> later = new System.Collections.Generic.HashSet<string>();
        internal static bool Later(MatchFile m) => later.Contains(m.id);

        static IEnumerator UploadLog(MatchFile m)
        {
            string text;
            try { text = System.IO.File.ReadAllText(m.backup); }
            catch (Exception e)
            {
                m.uploadError = "its log file can't be read";
                Store.SaveMatch(m);
                Plugin.Log.LogWarning("log non leggibile: " + e.Message);
                yield break;
            }
            var req = new Request("POST", "/api/replays", Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8");
            req.Header("X-File-Name", Uri.EscapeDataString(m.logName ?? "sim.log"));
            var played = PlayedAt(m.logName);
            if (played != null) req.Header("X-Played-At", played);
            // il tipo di partita (ladder, queue, private, solo) e la coda: sul sito servono al filtro della raccolta
            if (m.matchKind != null) req.Header("X-Match-Kind", m.matchKind);
            if (m.matchFormat != null) req.Header("X-Match-Format", Uri.EscapeDataString(m.matchFormat));
            yield return req.Send();
            if (req.Ok && (string)req.Body["replay"]?["id"] != null)
            {
                m.replayId = (string)req.Body["replay"]["id"];
                m.site = Site;
                Store.SaveMatch(m);
                Plugin.Log.LogInfo($"caricata: {m.logName} → {m.replayId}");
                yield break;
            }
            Plugin.Log.LogWarning($"caricamento di {m.logName}: {req.Status} {req.Problem}");
            if (req.Status == 401) { Unlinked(); yield break; }
            if (req.Ok || Temporary(req.Status)) { LastError = Unreachable(req); yield break; }
            if (req.Status != 400 && req.Status != 413) { later.Add(m.id); yield break; }
            // il sito non la vuole e non la vorrà mai (troppo corta, troppo grande): si segna e si passa alla prossima
            m.uploadError = req.Status == 400 ? "it's too short to be a game" : "its log is too big";
            Store.SaveMatch(m);
        }

        static IEnumerator UploadNotes(MatchFile m)
        {
            // per indice: mentre si carica puoi scrivere un'altra nota (la lista cresce), e va caricata anche lei
            for (int i = 0; i < m.notes.Count; i++)
            {
                var note = m.notes[i];
                if (note.uploaded) continue;
                var req = new Request("POST", "/api/replays/" + Uri.EscapeDataString(m.replayId) + "/notes", Json(new { line = note.line, body = note.text }));
                yield return req.Send();
                if (req.Status == 401) { Unlinked(); yield break; }
                if (!req.Ok && Temporary(req.Status)) { LastError = Unreachable(req); yield break; }
                // una risposta strana: la nota resta da caricare (l'ha scritta il giocatore), si riprova al prossimo avvio
                if (!req.Ok && req.Status != 400 && req.Status != 403 && req.Status != 404) { later.Add(m.id); yield break; }
                // caricata, oppure il sito non la vorrà mai (replay cancellato, riga fuori dal log, troppe note): non si riprova
                if (!req.Ok) Plugin.Log.LogWarning($"nota scartata dal sito: {req.Status} {req.Problem}");
                note.uploaded = true;
                Store.SaveMatch(m);
            }
        }

        // token revocato dal sito (Settings → Unlink) o utente non più attivo: si ricollega dal pulsante
        static void Unlinked()
        {
            Store.ForgetAccount();
            account = null;
            LastError = "Your OPTCG Trainer link was removed. Link again.";
        }

        // niente rete, sito sovraccarico o in manutenzione: problemi che passano da soli
        static bool Temporary(int status) => status == 0 || status == 429 || status >= 500;

        static string Unreachable(Request req) => req.Status == 0 ? "Couldn't reach OPTCG Trainer. Check your internet." : "OPTCG Trainer is busy right now. It'll retry later.";

        // il nome del file del sim è l'ora UTC della partita: 2026-11-07T11.48.39.8988538-<id>.log → millisecondi
        static string PlayedAt(string logName)
        {
            if (logName == null || logName.Length < 19) return null;
            if (!DateTime.TryParseExact(logName.Substring(0, 19), "yyyy-MM-dd'T'HH.mm.ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) return null;
            return ((long)(t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        }

        static byte[] Json(object value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));

        /// Una richiesta al sito, da aspettare in una coroutine.
        class Request
        {
            readonly UnityWebRequest web;
            internal int Status;
            internal JObject Body;
            internal string Problem;
            internal bool Ok => Status >= 200 && Status < 300;

            internal Request(string method, string path, byte[] body, string type = "application/json")
            {
                // niente rinvii: il token va solo all'indirizzo delle impostazioni
                web = new UnityWebRequest(Site + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 30, redirectLimit = 0 };
                if (body != null)
                {
                    web.uploadHandler = new UploadHandlerRaw(body);
                    web.SetRequestHeader("Content-Type", type);
                }
                // il token solo dove serve: le strade del collegamento non lo vogliono
                if (Linked && !path.StartsWith("/api/device/")) web.SetRequestHeader("Authorization", "Bearer " + account.token);
            }

            internal void Header(string name, string value) => web.SetRequestHeader(name, value);

            internal IEnumerator Send()
            {
                using (web)
                {
                    yield return web.SendWebRequest();
                    Status = web.result == UnityWebRequest.Result.ConnectionError ? 0 : (int)web.responseCode;
                    try { Body = JObject.Parse(web.downloadHandler.text); } catch { Body = new JObject(); }
                    Problem = (string)Body["error"] ?? (Status == 0 ? web.error : "HTTP " + Status);
                }
            }
        }
    }
}
