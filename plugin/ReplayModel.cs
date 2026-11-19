// Replay Room, seconda parte (C# puro, senza Unity): lo stato del tavolo mossa per mossa, come le liste del sim.
// Ogni carta (leader, carte del mazzo, DON) è un "pezzo" con un numero fisso: nel sim è sempre lo stesso GameObject.
// All'inizio i pezzi del mazzo non si sa cosa sono (il log nomina una carta solo quando si muove): la prima mossa
// che nomina un numero di copia (iid) prende il pezzo che sta in quel posto. Dato che il sim scrive i posti come indici
// delle sue liste (togli dal posto, inserisci nel posto: ReplaySync_EmitMove), rifare le mosse in ordine dà le stesse liste.
// Serve a due cose: dire alla Replay Room, prima di ogni mossa, dov'è la carta nel sim; e rifare lo stato a un punto
// qualsiasi per i salti indietro. Le righe RZ1|CHK (conteggi del sim dopo ogni mossa) dicono se torna.
using System;
using System.Collections.Generic;
using System.Linq;

namespace OPTCGTrainer
{
    sealed class ReplayPiece
    {
        public int owner;        // 1 o 2
        public bool don;
        public string card;      // l'id della carta, se si sa (i pezzi del mazzo mai mossi non si sanno)
        public int iid;
        public bool rested, faceUp, seen, sick;   // faceUp: life scoperta; seen: la vede chi ha il log (mano dell'avversario rivelata)
        public int pow, cost;    // le differenze di potenza e costo che scrive il sim (CardPower senza DON − potenza stampata)
        public int bottomGroup;
        public bool bottomCovered;

        public ReplayPiece Copy() => (ReplayPiece)MemberwiseClone();
    }

    /// Una mossa già capita: quale pezzo, da dove a dove (indici nelle liste prima di toglierlo e dopo averlo messo).
    struct ReplayStep
    {
        public int piece, player, fromZone, fromIndex, fromHost, toZone, toIndex, toHost;
        public bool moved;       // cambia lista o posto (se no cambia solo riposo/potenza)
    }

    sealed class ReplayModel
    {
        public readonly List<ReplayPiece> Pieces = new List<ReplayPiece>();
        // [giocatore 1..2][zona 0..8]: i pezzi in ordine, come le liste del sim (8 = il leader)
        public readonly List<int>[][] Zones = new List<int>[3][];
        // i DON attaccati: pezzo che li porta → pezzi DON (come CardLogicScript.lgo_AttachedDon)
        public readonly Dictionary<int, List<int>> Attached = new Dictionary<int, List<int>>();
        readonly Dictionary<int, int> byIid = new Dictionary<int, int>();
        public readonly List<string> Warnings = new List<string>();
        public int ChkChecked, ChkWrong;
        public readonly int[] DeckSize = new int[3], DonCount = new int[3];
        public readonly int[] Leader = new int[3];
        // Lunghezza del prefisso ricostruito a partire dal fondo (indice zero).
        public readonly int[] BottomCount = new int[3];
        readonly int[] lastPiece = { -1, -1, -1 };
        readonly int[] lastZone = { -1, -1, -1 };
        readonly List<string>[] pendingReveals = { null, new List<string>(), new List<string>() };
        readonly List<int>[] recentDeckDraws = { null, new List<int>(), new List<int>() };
        readonly int[] bottomGroupSeq = new int[3];
        readonly bool[] puttingUnder = new bool[3];

        public ReplayModel(ReplayLog log)
        {
            // quante carte e quanti DON ha ogni giocatore: le carte sono il massimo che si vede nelle righe CHK (all'inizio il mazzo
            // dell'avversario arriva dopo: la sua prima CHK dice 0). I DON sono il minimo: con un leader da 6 DON il sim
            // ne toglie 4 dal mazzo DON (SetDonMax) senza scrivere niente, e prima conta 10
            foreach (var e in log.Events.Where(e => e.kind == ReplayKind.Chk && (e.player == 1 || e.player == 2)))
            {
                var c = e.counts;
                DeckSize[e.player] = Math.Max(DeckSize[e.player], c[0] + c[1] + c[2] + c[3] + c[6] + c[7]);
                int don = c[4] + c[5] + c[9];
                if (don > 0) DonCount[e.player] = DonCount[e.player] == 0 ? don : Math.Min(DonCount[e.player], don);
            }
            for (int p = 1; p <= 2; p++)
            {
                if (DeckSize[p] == 0) DeckSize[p] = 50;
                if (DonCount[p] == 0) DonCount[p] = 10;
                Zones[p] = new List<int>[9];
                for (int z = 0; z < 9; z++) Zones[p][z] = new List<int>();
                Leader[p] = Add(new ReplayPiece { owner = p, card = log.Leaders[p] }, p, 8);
                for (int i = 0; i < DeckSize[p]; i++) Add(new ReplayPiece { owner = p }, p, 0);
                for (int i = 0; i < DonCount[p]; i++) Add(new ReplayPiece { owner = p, don = true, card = "Don" }, p, 4);
            }
        }

        int Add(ReplayPiece piece, int player, int zone)
        {
            Pieces.Add(piece);
            Zones[player][zone].Add(Pieces.Count - 1);
            return Pieces.Count - 1;
        }

        ReplayModel(ReplayModel from)
        {
            Pieces = from.Pieces.Select(x => x.Copy()).ToList();
            for (int p = 1; p <= 2; p++) Zones[p] = from.Zones[p].Select(z => new List<int>(z)).ToArray();
            foreach (var kv in from.Attached) Attached[kv.Key] = new List<int>(kv.Value);
            byIid = new Dictionary<int, int>(from.byIid);
            Array.Copy(from.DeckSize, DeckSize, 3);
            Array.Copy(from.DonCount, DonCount, 3);
            Array.Copy(from.Leader, Leader, 3);
            Array.Copy(from.BottomCount, BottomCount, 3);
            Array.Copy(from.lastPiece, lastPiece, 3);
            Array.Copy(from.lastZone, lastZone, 3);
            for (int p = 1; p <= 2; p++)
            {
                pendingReveals[p].AddRange(from.pendingReveals[p]);
                recentDeckDraws[p].AddRange(from.recentDeckDraws[p]);
            }
            Array.Copy(from.bottomGroupSeq, bottomGroupSeq, 3);
            Array.Copy(from.puttingUnder, puttingUnder, 3);
        }

        public ReplayModel Clone() => new ReplayModel(this);

        /// Dov'è un pezzo adesso: zona (9 = attaccato), indice nella sua lista, chi lo porta (per i DON attaccati).
        public (int zone, int index, int host) Find(int piece)
        {
            int p = Pieces[piece].owner;
            for (int z = 0; z < 9; z++)
            {
                int i = Zones[p][z].IndexOf(piece);
                if (i >= 0) return (z, i, -1);
            }
            foreach (var kv in Attached)
            {
                int i = kv.Value.IndexOf(piece);
                if (i >= 0) return (9, i, kv.Key);
            }
            return (-1, -1, -1);
        }

        // chi porta i DON nel posto "slot" della zona 9: 99 = il leader, se no il personaggio con quell'indice
        int Host(int player, int slot)
        {
            int h = slot / 100;
            if (h == 99) return Leader[player];
            var chars = Zones[player][2];
            return h >= 0 && h < chars.Count ? chars[h] : -1;
        }

        List<int> List(int player, int zone, int host) => zone == 9 ? (host >= 0 ? Get(host) : null) : zone >= 0 && zone < 9 ? Zones[player][zone] : null;

        List<int> Get(int host)
        {
            if (!Attached.TryGetValue(host, out var l)) Attached[host] = l = new List<int>();
            return l;
        }

        void Warn(ReplayEvent e, string what)
        {
            if (Warnings.Count < 200) Warnings.Add($"riga {e.line}: {what}");
        }

        /// Fa una mossa del log. Restituisce dove stava il pezzo e dove è andato (per rifare lo stesso nel sim).
        public ReplayStep Apply(ReplayEvent e)
        {
            var step = new ReplayStep { piece = -1, player = e.player };
            int p = e.player;
            if (p != 1 && p != 2) { Warn(e, "giocatore " + p); return step; }
            int piece = -1;
            if (e.dz == 8 || e.oz == 8) piece = Leader[p];
            else if (e.iid > 0 && byIid.TryGetValue(e.iid, out var known)) piece = known;
            else
            {
                // carta mai vista: è quella che sta in quel posto (e non deve avere già un numero)
                var from = List(p, e.oz, e.oz == 9 ? Host(p, e.os) : -1);
                int slot = e.oz == 9 ? e.os % 100 : e.os;
                if (from != null && slot >= 0 && slot < from.Count && Pieces[from[slot]].iid == 0) piece = from[slot];
                else if (from != null) foreach (var x in from) if (Pieces[x].iid == 0 && Pieces[x].don == (e.card == "Don")) { piece = x; break; }
                if (piece < 0) { Warn(e, $"{e.card}#{e.iid} non trovata in zona {e.oz}[{e.os}]"); return step; }
            }
            if (e.iid > 0 && Pieces[piece].iid != e.iid)
            {
                if (Pieces[piece].iid != 0) byIid.Remove(Pieces[piece].iid);
                Pieces[piece].iid = e.iid;
                byIid[e.iid] = piece;
            }
            var at = Find(piece);
            if (at.zone < 0) { Warn(e, $"{e.card}#{e.iid} non è in nessuna zona"); return step; }
            if (at.zone != e.oz) Warn(e, $"{e.card}#{e.iid} era in zona {at.zone}, il log dice {e.oz}");
            else if (e.oz != 9 && at.index != e.os) Warn(e, $"{e.card}#{e.iid} era in {e.oz}[{at.index}], il log dice [{e.os}]");
            step.piece = piece;
            step.fromZone = at.zone; step.fromIndex = at.index; step.fromHost = at.host;

            int toHost = e.dz == 9 ? Host(p, e.ds) : -1;
            if (e.dz == 9 && toHost < 0)
            {
                Warn(e, $"DON attaccato a un posto vuoto ({e.ds})");
                return step;
            }
            var dest = List(p, e.dz, toHost);
            if (dest == null) { Warn(e, "zona sconosciuta " + e.dz); return step; }
            int want = e.dz == 9 ? e.ds % 100 : e.ds;
            // lo stesso posto della stessa lista: cambia solo lo stato (riposo, potenza)
            bool same = e.dz == at.zone && (e.dz != 9 || toHost == at.host) && want == at.index;
            if (!same)
            {
                List(p, at.zone, at.host).RemoveAt(at.index);
                int to = Math.Max(0, Math.Min(want, dest.Count));
                dest.Insert(to, piece);
                step.toIndex = to;
                step.moved = true;
            }
            else step.toIndex = at.index;
            step.toZone = e.dz; step.toHost = toHost;

            var pc = Pieces[piece];
            if (!string.IsNullOrEmpty(e.card)) pc.card = e.card;
            bool field = e.dz == 2 || e.dz == 5 || e.dz == 7 || e.dz == 8;
            pc.rested = field && e.rest == 1;
            pc.pow = e.dz == 2 || e.dz == 7 || e.dz == 8 ? e.pow : 0;
            pc.cost = e.dz == 2 || e.dz == 7 || e.dz == 8 ? e.cost : 0;
            if (e.oz == 0 && e.dz == 1) recentDeckDraws[p].Add(piece);
            else recentDeckDraws[p].Clear();

            bool wasKnown = pc.seen;
            bool wasFaceUp = pc.faceUp;
            bool toBottom = e.dz == 0 && !e.shuffle && (step.toIndex == 0 || step.toIndex < BottomCount[p]);
            if (toBottom)
            {
                if (!puttingUnder[p])
                {
                    bottomGroupSeq[p]++;
                    puttingUnder[p] = true;
                }
                pc.bottomGroup = bottomGroupSeq[p];
                pc.bottomCovered = e.oz == 3 && !wasFaceUp;
            }
            else
            {
                puttingUnder[p] = false;
                if (e.dz != 0)
                {
                    pc.bottomGroup = 0;
                    pc.bottomCovered = false;
                }
            }

            if (step.fromZone == 0 && step.fromIndex < BottomCount[p]) BottomCount[p]--;
            if (toBottom) BottomCount[p]++;
            if (e.shuffle)
            {
                BottomCount[p] = 0;
                puttingUnder[p] = false;
                pendingReveals[p].Clear();
                recentDeckDraws[p].Clear();
                foreach (var idx in Zones[p][0])
                {
                    Pieces[idx].bottomGroup = 0;
                    Pieces[idx].bottomCovered = false;
                    Pieces[idx].seen = false;
                }
            }

            bool fromPendingReveal = !e.shuffle && e.oz == 0 && (e.dz == 1 || e.dz == 0)
                && !string.IsNullOrEmpty(pc.card) && pendingReveals[p].Remove(pc.card);
            bool publicSource = e.oz == 2 || e.oz == 6 || e.oz == 7 || e.oz == 8 || (e.oz == 3 && wasFaceUp);
            if (e.shuffle) pc.seen = false;
            else if (e.dz == 1) pc.seen = e.vp == 1 || publicSource || fromPendingReveal || (wasKnown && (e.oz == 1 || e.oz == 0));
            else if (e.dz == 0) pc.seen = fromPendingReveal || (wasKnown && e.oz == 0 && step.fromIndex == step.toIndex);
            else pc.seen = false;

            lastPiece[p] = piece;
            lastZone[p] = e.dz;
            pc.faceUp = e.dz == 3 && e.vp == 1 && e.ve == 1;
            // un personaggio appena entrato in campo è "stordito" (grigio) fino al prossimo turno del suo padrone (PlayerUntap)
            if (e.dz == 2 && at.zone != 2) pc.sick = true;
            if (e.dz != 2) pc.sick = false;
            // un personaggio che lascia il campo lascia anche i DON che portava (il sim li sposta prima, con le loro mosse)
            if (at.zone == 2 && e.dz != 2 && Attached.TryGetValue(piece, out var left) && left.Count > 0)
                Warn(e, $"{e.card}#{e.iid} lascia il campo con {left.Count} DON attaccati");
            return step;
        }

        static string ExtractCardId(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            const string tag = "link=\"";
            int start = s.IndexOf(tag, StringComparison.Ordinal);
            if (start < 0) return null;
            start += tag.Length;
            int end = s.IndexOf('"', start);
            return end > start ? s.Substring(start, end - start) : null;
        }

        public void Reveal(ReplayEvent e)
        {
            puttingUnder[1] = puttingUnder[2] = false;
            if (e.key != "Log.DrawFromTopDeck" && e.key != "Log.ActionReveal")
            {
                if (e.player == 1 || e.player == 2) recentDeckDraws[e.player].Clear();
                return;
            }
            if (e.player != 1 && e.player != 2) return;
            string id = ExtractCardId(e.s2);
            if (string.IsNullOrEmpty(id)) return;

            int p = e.player;
            if (e.key == "Log.DrawFromTopDeck")
            {
                var draws = recentDeckDraws[p];
                int match = -1;
                for (int k = 0; k < draws.Count; k++)
                {
                    var pc = Pieces[draws[k]];
                    if (pc.card == id && !pc.seen) { match = k; break; }
                }
                if (match >= 0)
                {
                    Pieces[draws[match]].seen = true;
                    draws.RemoveAt(match);
                    return;
                }
                for (int k = 0; k < draws.Count; k++)
                {
                    if (Pieces[draws[k]].card == id) { draws.RemoveAt(k); return; }
                }
                pendingReveals[p].Add(id);
                return;
            }

            int lp = lastPiece[p], lz = lastZone[p];
            if (lp >= 0 && (lz == 0 || lz == 1 || lz == 3) && Pieces[lp].card == id && !Pieces[lp].seen)
            {
                Pieces[lp].seen = true;
                if (lz == 3) Pieces[lp].faceUp = true;
                return;
            }
            if (lz != 0)
            {
                foreach (int owner in new[] { p, 3 - p })
                {
                    foreach (int idx in Zones[owner][1])
                    {
                        if (Pieces[idx].card == id && !Pieces[idx].seen)
                        {
                            Pieces[idx].seen = true;
                            return;
                        }
                    }
                }
            }
            if (lp >= 0 && (lz == 0 || lz == 1 || lz == 3) && Pieces[lp].card == id) return;
            pendingReveals[p].Add(id);
        }

        public List<List<ReplayPiece>> BottomGroups(int player)
        {
            var groups = new List<List<ReplayPiece>>();
            if (player != 1 && player != 2) return groups;
            var deck = Zones[player][0];
            int n = Math.Min(BottomCount[player], deck.Count);
            int current = -1;
            for (int i = n - 1; i >= 0; i--)
            {
                var pc = Pieces[deck[i]];
                if (groups.Count == 0 || pc.bottomGroup != current)
                {
                    groups.Add(new List<ReplayPiece>());
                    current = pc.bottomGroup;
                }
                groups[groups.Count - 1].Add(pc);
            }
            return groups;
        }

        public int KnownHandCount(int player) => player == 1 || player == 2 ? Zones[player][1].Count(i => Pieces[i].seen) : 0;

        /// Comincia il turno di un giocatore: i suoi personaggi non sono più storditi.
        public void TurnStarts(int player)
        {
            pendingReveals[1].Clear();
            pendingReveals[2].Clear();
            foreach (var x in Zones[player][2]) Pieces[x].sick = false;
        }

        /// Conteggi come nella riga CHK: mazzo, mano, campo, life, mazzo DON, DON, trash, stage, leader, DON attaccati.
        public int[] Counts(int p)
        {
            int equipped = Zones[p][8].Concat(Zones[p][2]).Sum(h => Attached.TryGetValue(h, out var l) ? l.Count : 0);
            return new[] { Zones[p][0].Count, Zones[p][1].Count, Zones[p][2].Count, Zones[p][3].Count, Zones[p][4].Count, Zones[p][5].Count,
                Zones[p][6].Count, Zones[p][7].Count, Zones[p][8].Count, equipped };
        }

        public bool Check(ReplayEvent chk)
        {
            if (chk.player != 1 && chk.player != 2) return true;
            // prima che arrivi il mazzo dell'avversario il sim conta 0 carte: non è una partita ancora
            if (chk.counts[0] + chk.counts[1] + chk.counts[2] + chk.counts[3] + chk.counts[6] + chk.counts[7] == 0) return true;
            var mine = Counts(chk.player);
            // i DON in più di prima di SetDonMax (sopra): finché nessun DON è uscito dal mazzo DON non è un errore
            if (chk.counts[4] > mine[4] && chk.counts[5] + chk.counts[9] == 0 && mine[5] + mine[9] == 0) return true;
            ChkChecked++;
            if (mine.SequenceEqual(chk.counts)) return true;
            ChkWrong++;
            Warn(chk, $"conteggi del giocatore {chk.player}: modello {string.Join(",", mine)}, sim {string.Join(",", chk.counts)}");
            return false;
        }

        /// Rifà le battute da beatFrom a beatTo (comprese); each riceve ogni mossa capita.
        public void Run(ReplayLog log, int beatFrom, int beatTo, Action<ReplayEvent, ReplayStep> each = null)
        {
            for (int b = beatFrom; b <= beatTo && b < log.Beats.Count; b++)
            {
                var beat = log.Beats[b];
                for (int i = beat.first; i <= beat.last; i++)
                {
                    var e = log.Events[i];
                    if (e.kind == ReplayKind.Move) { var s = Apply(e); each?.Invoke(e, s); }
                    else if (e.kind == ReplayKind.Chk) Check(e);
                    else if (e.kind == ReplayKind.Sem)
                    {
                        Reveal(e);
                        if (e.key == "Log.EndTurn") TurnStarts(beat.active == 1 ? 2 : 1);
                    }
                }
            }
        }

        /// Il modello dopo la battuta n (compresa); -1 = all'inizio.
        public static ReplayModel At(ReplayLog log, int beat)
        {
            var m = new ReplayModel(log);
            m.Run(log, 0, beat);
            return m;
        }
    }
}
