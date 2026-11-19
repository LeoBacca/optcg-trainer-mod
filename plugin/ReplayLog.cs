// Replay Room, prima parte (C# puro, senza Unity: si prova con tools/replay-test): dal testo di un combat log del sim
// alla lista ordinata degli eventi, raggruppati in "battute". Una battuta è quello che in partita succede insieme
// (le 5 carte della mano che volano, i DON pescati, la riga "Deploy" con la sua carta) e dopo la quale si aspetta un po'.
// Il ritmo è fisso (il log non ha orari): i tempi sono in Wait, a velocità 1.
//
// Le righe del log (scritte dal sim, GameplayLogicScript.ReplaySync_*):
//   RZ1|seq|giocatore|carta|daZona|daPosto|aZona|aPosto|vp|ve|riposo|deltaPotenza|deltaCosto|numeroCopia   una mossa
//   RZ1|HDR|… RZ1|META|… RZ1|PLY|n|nome|leader RZ1|CHK|seq|n|mazzo|mano|campo|life|mazzoDON|DON|trash|stage|leader|DONattaccati
//   RZ1|SEM|seq|giocatore|chiave|s1|s2|i1|i2 (base64)   RZ1|COMBAT|seq|n|attaccante|bersaglio   RZ1|DEFENDER|seq|n|carta|block/redirect
//   RZ1|END|seq|vincitore|motivo|hash   CHAT|base64   e le righe di testo del log a sinistra (con <mark><link=…> per le carte)
// Zone: 0 mazzo (in cima l'ultima), 1 mano, 2 personaggi, 3 life, 4 mazzo DON, 5 DON in campo, 6 trash, 7 stage, 8 leader,
// 9 DON attaccati (posto = indice del personaggio × 100 + indice del DON; 99 = leader).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OPTCGTrainer
{
    enum ReplayKind { Move, Text, Sem, Combat, Defender, Ply, Chk, End, Other }

    sealed class ReplayEvent
    {
        public ReplayKind kind;
        public int line;                 // riga del file, da 1
        public int player;               // 1 = chi ha il log, 2 = l'avversario (0 = nessuno, nelle SEM)
        // mossa
        public string card;
        public int oz, os, dz, ds, vp, ve, rest, pow, cost, iid;
        // testo: shown = la riga compare nel log a sinistra in partita (le righe di stato e le pescate il sim le scrive solo nel file)
        public string text;
        public bool shown;
        // SEM
        public string key, s1, s2;
        public int i1, i2;
        // COMBAT (iid = attaccante, target = bersaglio), DEFENDER (iid, block)
        public int target;
        public bool block;
        public bool shuffle;
        // PLY (text = nome, card = leader), CHK (counts), END (target = vincitore, text = motivo)
        public int[] counts;
    }

    sealed class ReplayBeat
    {
        public int first, last;          // eventi, compresi
        public float wait;               // secondi di pausa dopo la battuta, a velocità 1 (0 = non si vede niente: si passa oltre)
        public int turn;                 // il numero del turno come lo scrive il sim (Turn 3: tutti e due i giocatori)
        public int active;               // chi sta giocando (1 o 2)
        public int half;                 // il mezzo turno (ogni End Turn ne comincia uno): 0 prima del primo
    }

    sealed class ReplayLog
    {
        public readonly List<ReplayEvent> Events = new List<ReplayEvent>();
        public readonly List<ReplayBeat> Beats = new List<ReplayBeat>();
        public readonly string[] Names = { "", "", "" };
        public readonly string[] Leaders = { "", "", "" };
        public int Lines;                // righe del file
        public int Winner;               // dalla riga END (1, 2) o 0
        public string EndReason;
        public string Version;           // RZ1|HDR|<versione>
        public readonly List<int> HalfStarts = new List<int>();   // la prima battuta di ogni mezzo turno (indice 0 = mezzo turno 1)

        // le righe che il sim scrive solo nel file, mai nel log a sinistra (DrawCard_Internal, LogGameInfo, StartingHandKeep…)
        static readonly Regex Hidden = new Regex(@"^\[.*?\] (Drew card from deck: |Hand: |Board: |Trash: |Life: |Hand (?:before|after) Mulligan: )");

        public static ReplayLog Parse(string text)
        {
            var log = new ReplayLog();
            var raw = text.Replace("\r\n", "\n").Split('\n');
            log.Lines = raw.Length > 0 && raw[raw.Length - 1] == "" ? raw.Length - 1 : raw.Length;
            for (int i = 0; i < log.Lines; i++)
            {
                var e = ParseLine(raw[i], log);
                if (e == null) continue;
                e.line = i + 1;
                log.Events.Add(e);
            }
            log.MakeBeats();
            return log;
        }

        static int N(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static string B64(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch (FormatException) { return ""; }
        }

        static ReplayEvent ParseLine(string line, ReplayLog log)
        {
            if (line.Length == 0) return null;
            if (line.StartsWith("CHAT|", StringComparison.Ordinal))
                return new ReplayEvent { kind = ReplayKind.Text, text = B64(line.Substring(5)), shown = true };
            if (!line.StartsWith("RZ1|", StringComparison.Ordinal))
                return new ReplayEvent { kind = ReplayKind.Text, text = line, shown = !Hidden.IsMatch(line) };
            var f = line.Split('|');
            if (f.Length < 2) return null;
            switch (f[1])
            {
                case "HDR":
                    if (f.Length > 2) log.Version = f[2];
                    return new ReplayEvent { kind = ReplayKind.Other };
                case "PLY":
                    if (f.Length < 5) return null;
                    int who = N(f[2]);
                    if (who == 1 || who == 2)
                    {
                        if (f[3] != "") log.Names[who] = f[3];
                        if (f[4] != "") log.Leaders[who] = f[4];
                    }
                    return new ReplayEvent { kind = ReplayKind.Ply, player = who, text = f[3], card = f[4] };
                case "CHK":
                    if (f.Length < 14) return null;
                    var counts = new int[10];
                    for (int k = 0; k < 10; k++) counts[k] = N(f[4 + k]);
                    return new ReplayEvent { kind = ReplayKind.Chk, player = N(f[3]), counts = counts };
                case "SEM":
                    if (f.Length < 9) return null;
                    return new ReplayEvent { kind = ReplayKind.Sem, player = N(f[3]), key = B64(f[4]), s1 = B64(f[5]), s2 = B64(f[6]), i1 = N(f[7]), i2 = N(f[8]) };
                case "COMBAT":
                    if (f.Length < 6) return null;
                    return new ReplayEvent { kind = ReplayKind.Combat, player = N(f[3]), iid = N(f[4]), target = N(f[5]) };
                case "DEFENDER":
                    if (f.Length < 6) return null;
                    return new ReplayEvent { kind = ReplayKind.Defender, player = N(f[3]), iid = N(f[4]), block = f[5] == "block" };
                case "END":
                    if (f.Length < 5) return null;
                    log.Winner = N(f[3]);
                    log.EndReason = f[4];
                    return new ReplayEvent { kind = ReplayKind.End, target = N(f[3]), text = f[4] };
            }
            // una mossa: 14 campi (dalla IID1 c'è il numero della copia; i log senza non si possono rifare)
            if (f.Length < 14 || !int.TryParse(f[1], out _)) return new ReplayEvent { kind = ReplayKind.Other };
            return new ReplayEvent
            {
                kind = ReplayKind.Move, player = N(f[2]), card = f[3], oz = N(f[4]), os = N(f[5]), dz = N(f[6]), ds = N(f[7]),
                vp = N(f[8]), ve = N(f[9]), rest = N(f[10]), pow = N(f[11]), cost = N(f[12]), iid = N(f[13]),
            };
        }

        // due mosse vanno insieme (volano nello stesso momento, come in partita) se sono dello stesso giocatore e
        // dalla stessa zona alla stessa zona: le 5 carte della mano, i DON pescati, i DON riposati per pagare, la mescolata del mazzo
        static bool SameGroup(ReplayEvent a, ReplayEvent b) => a.player == b.player && a.oz == b.oz && a.dz == b.dz;

        // Le pause (velocità 1), scelte guardando una partita vera: una carta che vola ci mette ~0,3 s (CardLogicScript.MoveTo),
        // poi un attimo per vederla; la riga del log da sola si legge in fretta; attacco, risultato e fine turno si fermano di più.
        internal const float MoveWait = 0.55f, ShuffleWait = 0.3f, StateWait = 0.25f, TextWait = 0.45f;

        static float SemWait(string key)
        {
            switch (key)
            {
                case "Log.Attack": return 1.0f;
                case "Log.CombatResult": return 1.0f;
                case "Log.Counter": case "Log.EventCounter": case "Log.UseCounter": case "Log.Blocker": return 0.7f;
                case "Log.LeaderHit": case "Log.AttackFail": case "Log.Destroyed": return 0.8f;
                case "Log.EndTurn": return 1.2f;
                case "Log.Concede": return 1.0f;
                default: return 0;
            }
        }

        void MakeBeats()
        {
            var cur = new ReplayBeat { first = 0, last = -1 };
            ReplayEvent lastMove = null;
            bool hasText = false;
            float moveWait = 0, textWait = 0, semWait = 0;
            void Close(int upTo)
            {
                if (upTo < cur.first) return;
                cur.last = upTo;
                cur.wait = Math.Max(moveWait, textWait) + semWait;
                Beats.Add(cur);
                cur = new ReplayBeat { first = upTo + 1, last = -1 };
                lastMove = null;
                hasText = false;
                moveWait = textWait = semWait = 0;
            }
            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                switch (e.kind)
                {
                    case ReplayKind.Move:
                        if (lastMove != null && !SameGroup(lastMove, e)) Close(i - 1);
                        lastMove = e;
                        // una mossa che resta nello stesso posto cambia solo riposo o potenza; il mazzo rimescolato è un momento solo
                        float w = e.oz == 0 && e.dz == 0 ? ShuffleWait : e.oz == e.dz && e.os == e.ds ? StateWait : MoveWait;
                        moveWait = Math.Max(moveWait, w);
                        break;
                    case ReplayKind.Text when e.shown:
                    case ReplayKind.Sem:
                    case ReplayKind.Combat:
                    case ReplayKind.Defender:
                    case ReplayKind.End:
                        bool isText = e.kind == ReplayKind.Text;
                        // una SEM comincia l'azione dopo: chiude la battuta che ha già la sua riga; due righe di testo non vanno insieme
                        if (lastMove != null || ((isText || e.kind == ReplayKind.Sem || e.kind == ReplayKind.End) && hasText)) Close(i - 1);
                        if (isText) { hasText = true; textWait = TextWait; }
                        if (e.kind == ReplayKind.Sem) semWait += SemWait(e.key);
                        if (e.kind == ReplayKind.End) semWait += 2f;
                        break;
                }
            }
            Close(Events.Count - 1);

            // Una rimescolata riscrive gli indici 0,1,...; una search manda ogni carta al posto 0.
            foreach (var beat in Beats)
            {
                var deck = new List<ReplayEvent>();
                for (int i = beat.first; i <= beat.last; i++)
                    if (Events[i].kind == ReplayKind.Move && Events[i].oz == 0 && Events[i].dz == 0) deck.Add(Events[i]);
                if (deck.Count > 1 && deck[0].ds == 0 && deck[1].ds == 1)
                    foreach (var move in deck) move.shuffle = true;
            }

            // i turni: il primo comincia con la prima pescata di DON (dopo le mani e i mulligan), ogni End Turn passa all'altro.
            // Il numero sale quando finisce il turno di chi gioca per secondo, come fa il sim (EndTurn_Internal)
            int half = 0, active = 1, first = 0, turn = 0;
            for (int k = 0; k < Beats.Count; k++)
            {
                var b = Beats[k];
                for (int i = b.first; i <= b.last; i++)
                {
                    var e = Events[i];
                    if (half == 0 && e.kind == ReplayKind.Move && e.oz == 4 && e.dz == 5)
                    {
                        half = 1; first = e.player; active = first; turn = 1;
                        HalfStarts.Add(k);
                    }
                }
                b.turn = turn; b.active = active; b.half = half;
                // la fine del turno vale dalla battuta dopo: la riga "End Turn" si vede ancora nel turno che finisce
                for (int i = b.first; i <= b.last; i++)
                {
                    var e = Events[i];
                    if (half > 0 && e.kind == ReplayKind.Sem && e.key == "Log.EndTurn")
                    {
                        if (active != first) turn++;
                        active = active == 1 ? 2 : 1;
                        half++;
                        HalfStarts.Add(k + 1);
                    }
                }
            }
            // l'ultimo End Turn (se il log finisce lì) non comincia niente
            while (HalfStarts.Count > 0 && HalfStarts[HalfStarts.Count - 1] >= Beats.Count) HalfStarts.RemoveAt(HalfStarts.Count - 1);
        }

        /// La battuta da cui comincia il turno n del sim (quello di chi gioca per primo); -1 se non c'è.
        public int TurnStart(int turn)
        {
            for (int i = 0; i < Beats.Count; i++) if (Beats[i].turn == turn) return i;
            return -1;
        }
    }
}
