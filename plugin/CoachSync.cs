// Event Coach sul sito (0.5.0, contratto "coach-sync": docs/api-sito.md, punto 7). Il sito è solo il magazzino:
//   scarica  GET /api/coach/games?since=<rev>   le partite segnate a mano nell'estensione di Chrome (altri sim): in
//            coach.json (remote), contano nel coach come quelle aggiunte a mano; le lapidi le tolgono
//   carica   PUT /api/coach/state               lo stato del coach (evento, numeri fissati dei giorni, partite di Batsu
//            giorno per giorno) che l'estensione somma alle sue partite per mostrare lo stesso obiettivo del giorno
// Quando: alla partenza, aprendo il coach o Private, dopo una partita, e ogni 5 minuti (scarica); dopo che è cambiato
// qualcosa nel coach (carica: solo se lo stato è diverso da quello già mandato, al più ogni 10 secondi).
// Sim non collegato, sito giù o senza queste strade: il coach funziona come prima, niente a schermo (una riga nel log della mod).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OPTCGTrainer
{
    static class CoachSync
    {
        const float PullEvery = 300, PullGap = 20, PushGap = 10, PushCheckEvery = 600, RetryAfter = 60;
        static bool pulling, pushing, unsupported, pushWanted = true;
        static float nextPull, lastPull = -999, nextPush, nextCheck;
        static int seenVersion = -1;
        static string link, sent; // il collegamento di adesso; lo stato già mandato (senza l'ora)
        static bool pulledOnce;   // il primo GET di questo collegamento è riuscito
        static float firstTry = -1;
        const float SiteWait = 180;

        /// I numeri dei giorni si possono fissare (CoachStore.State): le partite dell'estensione sono arrivate (primo GET
        /// riuscito), o non ce ne possono essere (sim non collegato, sito senza queste strade), o il sito non risponde da 3
        /// minuti (il coach non resta senza numero per sempre). Se no il numero di oggi si fisserebbe senza le partite fatte
        /// ieri nel browser, troppo alto, e resterebbe così tutto il giorno.
        internal static bool SiteReady => !Account.Linked || unsupported || pulledOnce
            || (firstTry >= 0 && Time.realtimeSinceStartup - firstTry > SiteWait);

        /// In partita niente stato da calcolare (rilegge tutti i matches\*.json nel thread di Unity): si manda dopo.
        static bool Playing => GameUi.InGame && Match.Current != null && !Match.Current.Finished;

        /// Scarica presto (una schermata del coach o Private si apre, una partita è finita), ma non più di una volta ogni 20 secondi.
        internal static void PullSoon()
        {
            if (Time.realtimeSinceStartup - lastPull >= PullGap) nextPull = 0;
        }

        /// A ogni fotogramma (Plugin.Update): poco lavoro, le richieste sono coroutine come quelle di Account.
        internal static void Tick()
        {
            if (!Account.Linked || unsupported) return;
            float now = Time.realtimeSinceStartup;
            var current = Account.Site + "|" + Account.LinkId;
            if (current != link) { link = current; sent = null; nextPull = 0; pushWanted = true; pulledOnce = false; firstTry = -1; } // collegato adesso, o a un altro account
            if (!pulling && now >= nextPull) Plugin.Instance.StartCoroutine(Pull());
            if (CoachStore.Version != seenVersion) { seenVersion = CoachStore.Version; pushWanted = true; }
            // il giorno cambia alle 4 e i numeri si fissano senza che arrivi niente di nuovo: ogni tanto si guarda lo stesso
            if (now >= nextCheck) { nextCheck = now + PushCheckEvery; pushWanted = true; }
            if (pushWanted && !pushing && !pulling && now >= nextPush && !Playing) Push();
        }

        static System.Collections.IEnumerator Pull()
        {
            pulling = true;
            lastPull = Time.realtimeSinceStartup;
            nextPull = lastPull + PullEvery;
            if (firstTry < 0) firstTry = lastPull;
            try
            {
                var r = CoachStore.Data.remote;
                string site = Account.Site, account = Account.LinkId;
                long since = r.site == site && r.account == account ? r.rev : 0;
                int status = 0;
                JObject body = null;
                yield return Account.Call("GET", "/api/coach/games?since=" + since, null, (s, b) => { status = s; body = b; });
                if (status == 200 && body?["games"] is JArray list)
                {
                    var games = new List<RemoteGame>();
                    foreach (var item in list)
                    {
                        try { games.Add(item.ToObject<RemoteGame>()); }
                        catch (Exception) { /* una partita strana: si salta */ }
                    }
                    long rev = body["rev"]?.Type == JTokenType.Integer ? (long)body["rev"] : 0;
                    if (Account.Site == site && Account.LinkId == account)
                    {
                        CoachStore.MergeRemote(site, account, games, rev);
                        if (!pulledOnce) { pulledOnce = true; CoachStore.Version++; } // adesso i numeri dei giorni si possono fissare
                    }
                }
                else Problem("GET /api/coach/games", status, ref nextPull);
            }
            finally { pulling = false; }
        }

        static void Push()
        {
            nextPush = Time.realtimeSinceStartup + PushGap;
            // con un evento, i numeri giusti si sanno solo dopo aver letto i log: si aspetta (la lettura parte da State)
            var st = CoachUi.State;
            if (CoachStore.Data.active != null && (st == null || !CoachStore.Ready)) return;
            var state = Body(st);
            var text = JsonConvert.SerializeObject(state);
            pushWanted = false;
            if (text == sent) return;
            state["writtenAt"] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            Plugin.Instance.StartCoroutine(Put(state, text));
        }

        static System.Collections.IEnumerator Put(JObject state, string text)
        {
            pushing = true;
            try
            {
                int status = 0;
                yield return Account.Call("PUT", "/api/coach/state", state, (s, b) => status = s);
                if (status == 200) { sent = text; Plugin.Log.LogInfo("coach: stato mandato al sito"); }
                else { pushWanted = true; Problem("PUT /api/coach/state", status, ref nextPush); }
            }
            finally { pushing = false; }
        }

        // niente rete o sito occupato: si riprova tra un minuto. Un sito senza queste strade (403/404): basta, fino al prossimo avvio
        static void Problem(string what, int status, ref float next)
        {
            if (status == 403 || status == 404)
            {
                unsupported = true;
                Plugin.Log.LogInfo($"coach: il sito non ha ancora l'Event Coach ({what}: {status}), il coach resta solo nel sim");
                return;
            }
            if (status == -1) return; // non più collegato
            next = Time.realtimeSinceStartup + RetryAfter;
            Plugin.Log.LogInfo($"coach: {what} non riuscita ({status}), si riprova tra un minuto");
        }

        /// Lo stato per il sito (contratto, punto 2): l'evento con i numeri già fissati dei giorni, le partite di Batsu che
        /// contano giorno per giorno (quelle dell'estensione no: le somma chi legge), fin dove sono arrivate le partite del sito.
        static JObject Body(CoachState st)
        {
            var ev = st?.ev;
            var days = new JObject();
            if (st != null)
                foreach (var d in Coach.SiteDays(st))
                    days[d.Key] = new JObject { ["games"] = d.Value.games, ["wins"] = d.Value.wins, ["losses"] = d.Value.losses };
            return new JObject
            {
                ["v"] = 1,
                ["mod"] = Plugin.Version,
                ["event"] = ev == null ? JValue.CreateNull() : new JObject
                {
                    ["id"] = ev.id, ["name"] = ev.name, ["start"] = ev.start, ["eventDate"] = ev.eventDate, ["readyBy"] = ev.readyBy,
                    ["goal"] = ev.goal,
                    ["restDays"] = new JArray((ev.restDays ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal)),
                    ["leader"] = ev.leader,
                    ["leaderName"] = ev.leader != null ? CoachUi.LeaderName(ev.leader, st.leaderName) : null,
                    ["targets"] = JObject.FromObject(new SortedDictionary<string, int>(ev.targets ?? new Dictionary<string, int>(), StringComparer.Ordinal)),
                },
                ["days"] = days,
                ["gamesRev"] = CoachStore.Data.remote.rev,
            };
        }
    }
}
