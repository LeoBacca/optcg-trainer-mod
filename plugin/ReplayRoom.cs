// Replay Room (v0, solo dal telecomando: "replay <log>"): rivedere una partita dentro il sim, con le carte del sim che si
// muovono come in partita. Il log si legge con ReplayLog, lo stato si tiene con ReplayModel (C# puro); qui c'è il pezzo Unity.
//
// Come funziona, in breve:
//  - Ingresso: il tavolo di una partita Solo v Self senza partita (le stesse funzioni di GameStartSolo / LoadTurnState:
//    MakeMyCards, MakeMyLeader, MakeMyDon, SetupBoardObjects), un GameObject per ogni pezzo del modello, e il sim in
//    GameplayState.Observing: in quello stato GameplayStateCheck non fa niente (niente pescate, turni, scelte),
//    EmptyDeckCheck salta, i clic sulle carte aprono solo il trash; con ObservingCombat il sim disegna da solo la freccia
//    d'attacco con le due potenze (DrawAttackArrow), come quando si guarda una partita.
//  - Avanti: ogni mossa del log fa nel sim quello che fanno le sue funzioni *_Internal (DrawCard_Internal,
//    MoveHandCardToDeploy_Internal, AttachDonToCard, DiscardFromDeploy_Internal…): sposta il GameObject tra le liste
//    del giocatore nel posto scritto nel log, riposa/raddrizza, il suono di quel tipo di mossa, Refresh…Positions (la carta
//    vola con CardLogicScript.MoveTo). Non si chiamano le *_Internal vere: dipendono dagli effetti delle carte (inserire in
//    cima o in fondo, PlayerDeploysCharactersRested…) e il log dice già il posto giusto.
//  - Indietro e salti: lo stato si rifà dal modello (le liste del sim riempite di nuovo, senza distruggere né creare carte).
//  - La guardia (ReplayGuard, in fondo): finché la Replay Room è aperta il sim non scrive il suo log, non manda niente ai
//    suoi server, e la mod non registra niente (Match, coach, sito). Se un pezzo della guardia manca la Replay Room non si apre.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class ReplayRoom
    {
        /// La Replay Room è aperta: vale per la guardia, per Match e per i pulsanti della partita (GameUi).
        internal static bool Active { get; private set; }

        static GameplayLogicScript game;
        static ReplayLog log;
        static ReplayModel model;
        static string logPath;
        static GameObject[] gos;                                  // pezzo del modello → la sua carta nel sim
        static readonly Dictionary<GameObject, int> pieceOf = new Dictionary<GameObject, int>();
        static int done = -1;                                     // l'ultima battuta fatta (-1 = lo stato iniziale)
        internal enum HandView { Hidden, All, Known }
        static bool playing, fast;
        static HandView handMode;
        static float speed = 1, wait;
        static GameStyle styleBefore;
        // l'attacco in corso: da quale battuta, quando è arrivato il risultato e i due numeri del risultato
        static int attackBeat = -1, resultBeat = -1, resultAtt, resultDef;
        static int pendingAttacker, pendingTarget;                // dalle righe COMBAT e DEFENDER (numeri di copia, 0 = non si sa)
        static readonly GameObject[] lastRested = new GameObject[3]; // per giocatore: l'ultimo leader/personaggio riposato (chi attacca)
        // i controlli: liste del sim diverse dal modello a fine turno, mosse in un posto diverso da quello del modello
        static int checks, checksWrong, misplaced, errors;
        static readonly List<string> problems = new List<string>();

        static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        static object Call(string name, params object[] args)
        {
            if (!methods.TryGetValue(name, out var m))
            {
                m = AccessTools.Method(typeof(GameplayLogicScript), name) ?? throw new MissingMethodException("GameplayLogicScript." + name);
                methods[name] = m;
            }
            return m.Invoke(game, args);
        }

        static readonly AccessTools.FieldRef<GameplayLogicScript, GameObject> AttackerRef = AccessTools.FieldRefAccess<GameplayLogicScript, GameObject>("go_Attacker");
        static readonly AccessTools.FieldRef<GameplayLogicScript, GameObject> DefenderRef = AccessTools.FieldRefAccess<GameplayLogicScript, GameObject>("go_Defender");

        // il sim scrive nel suo Player.log anche righe che il programma OPBounty legge: le funzioni del sim
        // che chiamiamo per preparare e smontare il tavolo girano senza log di Unity (SetupBoardObjects scrive "Playing with deck")
        static void Quiet(Action a)
        {
            bool was = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try { a(); }
            finally { Debug.unityLogger.logEnabled = was; }
        }

        // ---- ingresso e uscita ----

        internal static string Open(string path)
        {
            if (Active) return "A replay is already open.";
            if (!ReplayGuard.Ready) return "Replay protection is unavailable. Missing: " + ReplayGuard.Missing;
            if (!File.Exists(path)) return "Replay file not found: " + path;
            var g = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
            if (g == null) return "The game board is unavailable (scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ")";
            var host = g.go_MenuManager != null ? g.go_MenuManager.GetComponent<HostJoinScript>() : null;
            if (host == null || host.go_Patchnotes == null || !host.go_Patchnotes.activeInHierarchy)
                return "Open the main menu before selecting a replay.";
            if (g.e_CurrentState != GameplayState.MainMenu || g.Lps_Players.Count > 0) return "Return to the main menu first (current state: " + g.e_CurrentState + ")";
            var parsed = ReplayLog.Parse(File.ReadAllText(path));
            if (!parsed.Events.Any(e => e.kind == ReplayKind.Move)) return "This file has no recorded moves. Older logs may not support playback.";
            game = g; log = parsed; logPath = path;
            model = new ReplayModel(log);
            Active = true; // prima di qualsiasi chiamata al sim: da qui la guardia è accesa
            try { Build(); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Replay Room non aperta: " + e);
                Exit();
                return "Could not open this replay.";
            }
            Plugin.Log.LogInfo($"Replay Room aperta: {Path.GetFileName(path)}, {log.Beats.Count} battute, {log.Lines} righe");
            return $"aperta: {Path.GetFileName(path)} · {log.Names[1]} ({log.Leaders[1]}) contro {log.Names[2]} ({log.Leaders[2]}) · {log.Beats.Count} battute · {log.Lines} righe";
        }

        static void Build()
        {
            done = -1; playing = false; fast = false; handMode = HandView.Hidden; wait = 0;
            attackBeat = resultBeat = -1; pendingAttacker = pendingTarget = 0;
            checks = checksWrong = misplaced = errors = 0; problems.Clear(); pieceOf.Clear(); sounds.Clear();
            Array.Clear(lastRested, 0, 3);
            styleBefore = game.e_GameStyle;
            // la carta che il modello sa per ogni pezzo alla fine della partita: quella che il pezzo è stato dall'inizio
            // (un pezzo del mazzo che non si è mai mosso prende una carta qualsiasi dello stesso giocatore: resta coperto)
            var end = ReplayModel.At(log, log.Beats.Count - 1);
            Quiet(() =>
            {
                var menu = game.go_MenuManager.GetComponent<HostJoinScript>();
                menu.SetMain(false);
                menu.SetSolo(false);
                game.e_GameStyle = GameStyle.SoloVSelf;     // nessuna chiamata di rete nelle funzioni del sim
                game.e_CurrentState = GameplayState.Observing;
                ClearLogPanel();
                game.MakePlayersIfNecessary();
                gos = new GameObject[model.Pieces.Count];
                for (int p = 1; p <= 2; p++)
                {
                    var ps = game.Lps_Players[p - 1];
                    game.MakeMyCards(ps, p - 1, model.DeckSize[p]);
                    game.MakeMyLeader(ps, p - 1);
                    Call("MakeMyDon", ps, p - 1);
                    Call("MakeMyEmptyDecks", ps);
                    ps.s_PlayerName = log.Names[p];
                    // i DON: quanti ne dice il log (un leader da 6 DON: il sim ne toglie dal mazzo DON, come SetDonMax)
                    while (ps.Lgo_MyDonDeck.Count > model.DonCount[p])
                    {
                        UnityEngine.Object.Destroy(ps.Lgo_MyDonDeck[0]);
                        ps.Lgo_MyDonDeck.RemoveAt(0);
                    }
                    string fallback = end.Pieces.Where(x => x.owner == p && !x.don && x.card != null).Select(x => x.card).FirstOrDefault(Known) ?? log.Leaders[p];
                    int deck = 0, don = 0;
                    for (int i = 0; i < model.Pieces.Count; i++)
                    {
                        var piece = model.Pieces[i];
                        if (piece.owner != p) continue;
                        GameObject go = i == model.Leader[p] ? ps.Lgo_MyLeader[0] : piece.don ? ps.Lgo_MyDonDeck[don++] : ps.Lgo_MyDeck[deck++];
                        gos[i] = go;
                        pieceOf[go] = i;
                        if (!piece.don) LoadDef(go, end.Pieces[i].card is string c && Known(c) ? c : fallback);
                    }
                    game.go_P0Name.GetComponent<TextMeshProUGUI>().text = log.Names[1];
                    game.go_P1Name.GetComponent<TextMeshProUGUI>().text = log.Names[2];
                }
                Call("SetupBoardObjects");
                // quello che in una partita serve a giocarla (o a mandarla a Batsu) qui non c'è: resta Back to Main (= esci)
                foreach (var go in new[] { game.go_DownloadLog, game.go_ReportBug, game.go_RequestCancelBtn, game.go_SaveGameState, game.go_Concede,
                    game.go_GameStateBackTurn, game.go_GameStateForwardTurn, game.go_GameStateRestartTurn })
                    if (go != null) go.SetActive(false);
                game.RemoveChoices();
            });
            ApplyState(model, instant: true);
            ShowTurn(-1);
            ReplayBar.Build(game);
            ReplayStudy.Build(game, logPath);
        }

        static bool Known(string id) => !string.IsNullOrEmpty(id) && game.card_Database.dict_Cards.ContainsKey(id);

        static void LoadDef(GameObject go, string id)
        {
            var cls = go.GetComponent<CardLogicScript>();
            if (!Known(id) || cls.myCard.cardDef != null && cls.myCard.cardDef.cardID == id) return;
            cls.LoadCardDefinition(game.card_Database.FindDefinition(id));
            go.name = id;
        }

        /// Chiude la Replay Room e torna al menu come Back to Main (ReturnToMain), senza le sue righe di log e la rete.
        internal static void Exit()
        {
            if (!Active) return;
            playing = false;
            Recorder.Stop();
            try
            {
                ReplayStudy.Close();
                ReplayBar.Destroy();
                if (game != null)
                {
                    Quiet(() =>
                    {
                        game.go_P0Name.GetComponent<TextMeshProUGUI>().text = "";
                        game.go_P1Name.GetComponent<TextMeshProUGUI>().text = "";
                        game.iCounterAmount = 0;
                        game.e_CurrentState = GameplayState.MainMenu;
                        game.CleanupMatch();   // distrugge le carte, spegne i pulsanti del tavolo, azzera il registro (con la guardia accesa)
                        game.go_P0Board.SetActive(false);
                        game.go_P1Board.SetActive(false);
                        game.go_LogView.SetActive(false);
                        game.go_ChatBox.SetActive(false);
                        game.go_ChatButton.SetActive(false);
                        game.go_ChatMute.SetActive(false);
                        game.text_GuideText.text = "";
                        ClearLogPanel();   // le righe del replay non restano nel log della prossima partita
                        game.e_GameStyle = styleBefore;
                        var menu = game.go_MenuManager.GetComponent<HostJoinScript>();
                        menu.SetMain(true);
                        menu.SetMultiRematch(false);
                    });
                }
            }
            catch (Exception e) { Plugin.Log.LogError("uscita dalla Replay Room: " + e); }
            finally
            {
                Active = false; // la guardia si spegne solo a tavolo smontato
                gos = null; pieceOf.Clear(); model = null;
                Plugin.Log.LogInfo("Replay Room chiusa");
            }
        }

        static void ClearLogPanel()
        {
            var content = game.go_LogView.transform.GetChild(0).GetChild(0);
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                child.transform.SetParent(null);
                UnityEngine.Object.Destroy(child);
            }
        }

        // ---- a ogni fotogramma ----

        internal static void Tick()
        {
            ReplayLibrary.FollowMenu();
            Tests.Tick();
            if (!Active) return;
            if (game == null) { Active = false; return; } // scena cambiata sotto di noi: il tavolo non c'è più
            if (ReplayLibrary.Visible) return;
            if (!ReplayStudy.Typing) Keys();
            if (fast)
            {
                // per le prove: tutto il log, 8 battute per fotogramma, con le stesse funzioni (le carte non fanno in tempo a volare)
                for (int k = 0; k < 8 && done < log.Beats.Count - 1; k++) Step();
                if (done >= log.Beats.Count - 1) fast = false;
            }
            else if (playing)
            {
                wait -= Time.deltaTime * speed;
                while (wait <= 0 && playing)
                {
                    if (done >= log.Beats.Count - 1) { playing = false; break; }
                    Step();
                    wait += log.Beats[done].wait;
                }
            }
            ReplayBar.Refresh(Status(false));
            ReplayStudy.Refresh();
        }

        static void Keys()
        {
            if (Input.GetKeyDown(KeyCode.D)) ReplayStudy.ToggleDraw();
            if (Input.GetKeyDown(KeyCode.N)) ReplayStudy.ToggleNotes();
            if (Input.GetKeyDown(KeyCode.B)) ReplayStudy.ToggleBottom();
            if (Input.GetKeyDown(KeyCode.H)) ToggleHand();
            if (Input.GetKeyDown(KeyCode.Space)) Toggle();
            if (Input.GetKeyDown(KeyCode.RightArrow)) Forward();
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Back();
            if (Input.GetKeyDown(KeyCode.UpArrow)) Turn(+1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) Turn(-1);
            // T ordina il trash aperto, come in partita (il sim legge i suoi tasti solo fuori dalla Replay Room: ReplayGuard)
            if (Input.GetKeyDown(KeyCode.T)) game.SortTrash();
        }

        internal static void Toggle()
        {
            if (!Active) return;
            if (done >= log.Beats.Count - 1) { JumpTo(-1); }
            playing = !playing;
            if (playing) ReplayStudy.EndDrawing();
            wait = 0;
        }

        internal static void Pause() => playing = false;

        /// Un passo avanti: le battute fino alla prima che si vede.
        internal static void Forward()
        {
            if (!Active) return;
            playing = false;
            while (done < log.Beats.Count - 1)
            {
                Step();
                if (log.Beats[done].wait > 0) break;
            }
        }

        /// Un passo indietro: lo stato dopo la battuta visibile prima di quella di adesso.
        internal static void Back()
        {
            if (!Active) return;
            playing = false;
            int b = done - 1;
            while (b >= 0 && log.Beats[b].wait <= 0) b--;
            JumpTo(b);
        }

        /// Il turno dopo (o prima) di un giocatore: ogni End Turn ne comincia uno. Indietro: all'inizio del turno di adesso,
        /// o di quello prima se si è già all'inizio.
        internal static void Turn(int dir)
        {
            if (!Active) return;
            playing = false;
            int next = done + 1; // la prossima battuta da fare
            var starts = log.HalfStarts;
            if (dir > 0)
            {
                int target = -1;
                foreach (var st in starts) if (st > next) { target = st; break; }
                JumpTo(target < 0 ? log.Beats.Count - 1 : target - 1);
                return;
            }
            int cur = -1, prev = -1;
            foreach (var st in starts) if (st <= next) { prev = cur; cur = st; }
            if (cur >= 0 && next > cur) JumpTo(cur - 1);
            else JumpTo(prev >= 0 ? prev - 1 : -1);
        }

        /// All'inizio del turno n come lo scrive il sim (Turn n).
        internal static string GoTurn(int n)
        {
            int b = log.TurnStart(n);
            if (b < 0) return "il turno " + n + " non c'è (ultimo: " + log.Beats.Last().turn + ")";
            playing = false;
            JumpTo(b - 1);
            return "turno " + n;
        }

        internal static void SetSpeed(float s)
        {
            speed = Mathf.Clamp(s, 0.25f, 16f);
        }

        internal static float Speed => speed;
        internal static bool Playing => playing;
        internal static HandView HandMode => handMode;
        internal static bool RevealHand => handMode != HandView.Hidden;
        internal static string HandButtonLabel => handMode switch
        {
            HandView.All => "Show known",
            HandView.Known => "Hide hand",
            _ => "Show hand",
        };
        internal static int Moment => done;
        internal static int TotalBeats => log != null ? log.Beats.Count : 0;
        internal static int TurnAt(int b) => log != null && b >= 0 && b < log.Beats.Count ? log.Beats[b].turn : 0;
        internal static ReplayModel Board => model;
        internal static string CurrentLogPath => Active ? logPath : null;
        internal static void Seek(int b) { Pause(); JumpTo(b); }

        internal static void ToggleHand()
        {
            handMode = handMode switch
            {
                HandView.Hidden => HandView.All,
                HandView.All => HandView.Known,
                _ => HandView.Hidden,
            };
            if (Active) FixFaces();
        }

        internal static void SetHand(HandView mode)
        {
            handMode = mode;
            if (Active) FixFaces();
        }

        // ---- una battuta avanti, come la fa il sim ----

        static void Step()
        {
            int b = done + 1;
            try { ApplyBeat(b); }
            catch (Exception e)
            {
                errors++;
                Problem($"battuta {b} (riga {log.Events[log.Beats[b].first].line}): {e.GetType().Name} {e.Message}");
                Plugin.Log.LogError("Replay Room, battuta " + b + ": " + e);
            }
            done = b;
        }

        static readonly HashSet<string> Outcomes = new HashSet<string> { "Log.LeaderHit", "Log.AttackFail", "Log.Destroyed" };

        // l'attacco finisce alla battuta dopo il suo risultato (o dopo l'esito che lo segue: colpito, fallito, KO)
        static void AttackOver(int b)
        {
            if (attackBeat < 0 || resultBeat < 0 || b <= resultBeat) return;
            var beat = log.Beats[b];
            bool outcome = Enumerable.Range(beat.first, beat.last - beat.first + 1).Any(i => log.Events[i].kind == ReplayKind.Sem && Outcomes.Contains(log.Events[i].key));
            if (!outcome || b > resultBeat + 1) EndAttack();
        }

        // le righe che guidano la freccia (COMBAT, DEFENDER, le SEM dell'attacco) e chi si è appena riposato (chi attacca)
        static void Combat(ReplayEvent e, int b, bool live)
        {
            switch (e.kind)
            {
                case ReplayKind.Move:
                    if ((e.dz == 8 || e.dz == 2) && e.rest == 1 && (e.player == 1 || e.player == 2)) lastRested[e.player] = Go(e.iid) ?? lastRested[e.player];
                    break;
                case ReplayKind.Combat: pendingAttacker = e.iid; pendingTarget = e.target; break;
                case ReplayKind.Defender:
                    if (e.block && attackBeat >= 0 && Go(e.iid) is GameObject blocker) DefenderRef(game) = blocker;
                    else if (!e.block) pendingTarget = e.iid;
                    break;
                case ReplayKind.Sem: Sem(e, b, live); break;
            }
        }

        static void ApplyBeat(int b)
        {
            var beat = log.Beats[b];
            AttackOver(b);
            bool draw = false, deploy = false, discard = false, trash = false, endTurn = false;
            for (int i = beat.first; i <= beat.last; i++)
            {
                var e = log.Events[i];
                switch (e.kind)
                {
                    case ReplayKind.Move:
                        var step = model.Apply(e);
                        if (step.piece < 0) break;
                        SimMove(e, step);
                        if (!step.moved) break;
                        int fz = step.fromZone, tz = step.toZone;
                        // i suoni come le funzioni del sim: in mano audDraw (DrawCard, DrawFromLife, DrawCardFromTrash), in campo
                        // audDeploy, nel trash audDiscard; DON attaccato audDraw (AttachDonToCard), DON restituito audDiscard (ReturnDon)
                        if (tz == 1 && fz != 1 || tz == 9 && fz == 5) draw = true;
                        if ((tz == 2 || tz == 7) && fz != tz) deploy = true;
                        if (tz == 6 && fz != 6 || tz == 4 && (fz == 5 || fz == 9)) discard = true;
                        if (tz == 6 || fz == 6) trash = true;
                        break;
                    case ReplayKind.Chk: model.Check(e); break;
                    case ReplayKind.Sem: model.Reveal(e); break;
                    case ReplayKind.Text when e.shown: game.AddLocalLogLine(e.text, -1); break;
                    case ReplayKind.End: ShowEnd(); break;
                }
                Combat(e, b, live: true);
                if (e.kind == ReplayKind.Sem && e.key == "Log.EndTurn") endTurn = true;
            }
            if (attackBeat >= 0 && (!OnField(AttackerRef(game)) || !OnField(DefenderRef(game)))) EndAttack();
            if (draw) Sound(game.audDraw, "draw");
            if (deploy) Sound(game.audDeploy, "deploy");
            if (discard) Sound(game.audDiscard, "discard");
            RefreshAll(trash);
            ShowTurn(b);
            if (endTurn) Verify(b);
        }

        static bool OnField(GameObject go) => go != null && game.Lps_Players.Any(ps => ps.Lgo_MyLeader.Contains(go) || ps.Lgo_MyDeploy.Contains(go));

        // i suoni partiti (e quanti risultavano davvero in riproduzione): il laboratorio è muto, si controllano da qui
        static readonly Dictionary<string, (int played, int playing)> sounds = new Dictionary<string, (int, int)>();

        static void Sound(AudioSource source, string name)
        {
            if (source == null) return;
            source.Play();
            sounds.TryGetValue(name, out var n);
            sounds[name] = (n.played + 1, n.playing + (source.isPlaying ? 1 : 0));
        }

        static GameObject Go(int iid)
        {
            for (int i = 0; i < model.Pieces.Count; i++) if (model.Pieces[i].iid == iid) return gos[i];
            return null;
        }

        static List<GameObject> SimList(int player, int zone, int host)
        {
            var ps = game.Lps_Players[player - 1];
            switch (zone)
            {
                case 0: return ps.Lgo_MyDeck;
                case 1: return ps.Lgo_MyHand;
                case 2: return ps.Lgo_MyDeploy;
                case 3: return ps.Lgo_MyLifeDeck;
                case 4: return ps.Lgo_MyDonDeck;
                case 5: return ps.Lgo_MyDonCostArea;
                case 6: return ps.Lgo_MyTrash;
                case 7: return ps.Lgo_MyStage;
                case 8: return ps.Lgo_MyLeader;
                case 9: return host >= 0 ? gos[host].GetComponent<CardLogicScript>().lgo_AttachedDon : null;
            }
            return null;
        }

        // la mossa nel sim: il GameObject dalla lista di partenza a quella d'arrivo, nel posto del log, con lo stato del modello
        static void SimMove(ReplayEvent e, ReplayStep step)
        {
            var go = gos[step.piece];
            var piece = model.Pieces[step.piece];
            var cls = go.GetComponent<CardLogicScript>();
            if (step.moved)
            {
                var from = SimList(step.player, step.fromZone, step.fromHost);
                int at = from != null ? from.IndexOf(go) : -1;
                if (at != step.fromIndex)
                {
                    misplaced++;
                    Problem($"riga {e.line}: {e.card} nel sim era in {step.fromZone}[{at}], nel modello [{step.fromIndex}]");
                }
                if (at >= 0) from.RemoveAt(at);
                else RemoveEverywhere(go);
                var to = SimList(step.player, step.toZone, step.toHost);
                to.Insert(Math.Min(step.toIndex, to.Count), go);
                if (step.toZone == 1 || step.fromZone == 1) HandOrder(step.player);
            }
            if (!string.IsNullOrEmpty(e.card) && e.card != "Don") LoadDef(go, e.card);
            cls.myCard.bTapped = piece.rested;
            cls.myCard.bSummonSick = piece.sick;
            cls.myCard.bForcedFaceUp = piece.faceUp;
        }

        static void RemoveEverywhere(GameObject go)
        {
            foreach (var ps in game.Lps_Players)
            {
                foreach (var l in new[] { ps.Lgo_MyDeck, ps.Lgo_MyHand, ps.Lgo_MyDeploy, ps.Lgo_MyLifeDeck, ps.Lgo_MyDonDeck, ps.Lgo_MyDonCostArea, ps.Lgo_MyTrash, ps.Lgo_MyStage })
                    l.Remove(go);
                foreach (var host in ps.Lgo_MyLeader.Concat(ps.Lgo_MyDeploy)) host.GetComponent<CardLogicScript>().lgo_AttachedDon.Remove(go);
            }
        }

        // la mano si vede nell'ordine della lista (il sim la mette in fila con iHandUIOrder: la pescata va in fondo)
        static void HandOrder(int player)
        {
            var hand = game.Lps_Players[player - 1].Lgo_MyHand;
            for (int i = 0; i < hand.Count; i++) hand[i].GetComponent<CardLogicScript>().myCard.iHandUIOrder = i;
        }

        static void RefreshAll(bool trashChanged, bool instant = false)
        {
            // come le *_Internal: dopo ogni mossa le posizioni della zona toccata; qui tutte, è lo stesso conto per il sim
            // RefreshLeaderPositions rimette il leader dritto: prima di RefreshDeployPositions, che lo riposa se attacca
            game.RefreshLeaderPositions();
            game.RefreshLifePositions();
            Call("RefreshDeployPositions");
            Call("RefreshDonPositions");
            Call("RefreshStagePositions");
            game.RefreshDeckPositions(instant);
            game.RefreshHandPositions(instant);
            // il trash cambiato chiude la sua vista, come CleanUpTrashView nelle funzioni del sim
            if (trashChanged) game.CleanUpTrashView();
            else Call("RefreshTrashPositions");
            FixFaces();
            if (instant)
                foreach (var go in gos)
                    if (go != null) go.transform.localPosition = go.GetComponent<CardLogicScript>().vDestination;
        }

        /// Le carte coperte come le vede chi ha il log: la mano dell'avversario coperta (salvo le carte rivelate, o "Hand"),
        /// in Solo v Self il sim la mostrerebbe scoperta (SetupHandCard). Si rifà anche dopo ogni RefreshHandPositions del sim.
        internal static void FixFaces()
        {
            if (!Active || model == null || game.Lps_Players.Count < 2) return;
            foreach (var go in game.Lps_Players[1].Lgo_MyHand)
            {
                if (!pieceOf.TryGetValue(go, out var i)) continue;
                var piece = model.Pieces[i];
                bool up = (handMode == HandView.All || (handMode == HandView.Known && piece.seen)) && Known(piece.card);
                if (up) LoadDef(go, piece.card);
                var cls = go.GetComponent<CardLogicScript>();
                cls.SetFaceUp(up);
            }
        }

        // ---- le righe SEM: attacco, counter, risultato, fine turno ----

        static void Sem(ReplayEvent e, int b, bool live)
        {
            switch (e.key)
            {
                case "Log.Attack":
                    int att = e.player == 1 || e.player == 2 ? e.player : log.Beats[b].active;
                    int def = att == 1 ? 2 : 1;
                    // chi attacca: la riga COMBAT (la scrive chi riceve l'attacco), se no l'ultimo che si è riposato, se no il leader
                    var attacker = (pendingAttacker > 0 ? Go(pendingAttacker) : null) ?? lastRested[att] ?? gos[model.Leader[att]];
                    var defender = (pendingTarget > 0 ? Go(pendingTarget) : null) ?? gos[model.Leader[def]];
                    AttackerRef(game) = attacker;
                    DefenderRef(game) = defender;
                    game.iCounterAmount = 0;
                    if (live) Sound(game.audAttack, "attack");
                    game.e_CurrentState = GameplayState.ObservingCombat; // il sim disegna la freccia e le due potenze (DrawAttackArrow)
                    attackBeat = b; resultBeat = -1;
                    pendingAttacker = pendingTarget = 0;
                    break;
                case "Log.Counter":
                case "Log.EventCounter":
                    if (attackBeat >= 0 && e.i1 > 0) game.iCounterAmount += e.i1; // il "+2000" sul difensore (CardLogicScript, figlio 2)
                    break;
                case "Log.CombatResult":
                    if (attackBeat < 0) break;
                    resultBeat = b; resultAtt = e.i1; resultDef = e.i2;
                    break;
                case "Log.EndTurn":
                    EndAttack();
                    if (!live) break; // nei salti il modello è già rifatto (ReplayModel.Run)
                    model.TurnStarts(log.Beats[b].active == 1 ? 2 : 1);
                    foreach (var go in game.Lps_Players[log.Beats[b].active == 1 ? 1 : 0].Lgo_MyDeploy)
                        go.GetComponent<CardLogicScript>().myCard.bSummonSick = false;
                    break;
            }
        }

        static void EndAttack()
        {
            if (attackBeat < 0) return;
            attackBeat = resultBeat = -1;
            game.iCounterAmount = 0;
            game.e_CurrentState = GameplayState.Observing;
        }

        /// I numeri sulla freccia: dopo la riga del risultato sono quelli del log (CombatResult), prima quelli del sim.
        internal static void FixArrow()
        {
            if (!Active || resultBeat < 0) return;
            var att = AttackerRef(game);
            bool mine = att != null && game.FindCardOwner(att) == game.Lps_Players[0];
            game.go_P0Attack.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = (mine ? resultAtt : resultDef).ToString(CultureInfo.InvariantCulture);
            game.go_P1Attack.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = (mine ? resultDef : resultAtt).ToString(CultureInfo.InvariantCulture);
        }

        /// La potenza di una carta del replay: la stampata più la differenza del log, più 1000 per DON quando attacca nel suo
        /// turno (come CardPower). -1 = non è una carta del replay (si calcola come sempre).
        internal static int Power(GameObject go, bool attacking, bool ignoreDon)
        {
            if (!Active || model == null || go == null || !pieceOf.TryGetValue(go, out var i)) return -1;
            var cls = go.GetComponent<CardLogicScript>();
            if (cls.myCard.cardDef == null) return -1;
            int don = attacking && !ignoreDon ? cls.lgo_AttachedDon.Count * 1000 : 0;
            return cls.myCard.cardDef.cardPower + model.Pieces[i].pow + don;
        }

        internal static int Cost(GameObject go)
        {
            if (!Active || model == null || go == null || !pieceOf.TryGetValue(go, out var i)) return -1;
            var cls = go.GetComponent<CardLogicScript>();
            return cls.myCard.cardDef == null ? -1 : cls.myCard.cardDef.cardCost + model.Pieces[i].cost;
        }

        static void ShowTurn(int b)
        {
            // il turno di adesso: quello della battuta dopo (una battuta con End Turn finisce il turno)
            var next = b + 1 < log.Beats.Count ? log.Beats[b + 1] : log.Beats[Math.Max(0, b)];
            game.gsv_CurrentGame.iTurnNumber = next.turn;
            game.gsv_CurrentGame.iPlayerTurn = game.gsv_CurrentGame.iPlayerAction = next.active - 1;
            game.text_TurnCount.text = next.turn > 0 ? game.tls_Translation.Translate("Title.Turn") + next.turn : "";
        }

        static void ShowEnd()
        {
            if (log.Winner == 1) game.go_YouWin.SetActive(true);
            else if (log.Winner == 2) game.go_YouLose.SetActive(true);
        }

        // ---- salti ----

        static void JumpTo(int b)
        {
            b = Math.Max(-1, Math.Min(b, log.Beats.Count - 1));
            EndAttack();
            game.go_YouWin.SetActive(false);
            game.go_YouLose.SetActive(false);
            model = ReplayModel.At(log, b);
            ApplyState(model, instant: true);
            done = b;
            Array.Clear(lastRested, 0, 3);
            pendingAttacker = pendingTarget = 0;
            // un salto nel mezzo di un attacco: freccia, counter e risultato rifatti dalle righe del turno di adesso (senza suoni)
            int from = 0;
            foreach (var st in log.HalfStarts) if (st <= b) from = st;
            for (int k = from; k <= b; k++)
            {
                AttackOver(k);
                for (int i = log.Beats[k].first; i <= log.Beats[k].last; i++) Combat(log.Events[i], k, live: false);
            }
            // il log a sinistra: le ultime righe fino a qui
            ClearLogPanel();
            int lastEvent = b >= 0 ? log.Beats[b].last : -1;
            var lines = log.Events.Take(lastEvent + 1).Where(e => e.kind == ReplayKind.Text && e.shown).Select(e => e.text).ToList();
            foreach (var t in lines.Skip(Math.Max(0, lines.Count - 100))) game.AddLocalLogLine(t, -1);
            if (b >= 0 && log.Events.Skip(log.Beats[b].first).Take(lastEvent - log.Beats[b].first + 1).Any(e => e.kind == ReplayKind.End)) ShowEnd();
            ShowTurn(b);
            wait = 0;
        }

        // le liste del sim riempite come dice il modello (stesse carte, niente create o distrutte), poi tutte al loro posto
        static void ApplyState(ReplayModel m, bool instant)
        {
            for (int p = 1; p <= 2; p++)
            {
                for (int z = 0; z < 9; z++)
                {
                    var list = SimList(p, z, -1);
                    list.Clear();
                    list.AddRange(m.Zones[p][z].Select(x => gos[x]));
                }
                HandOrder(p);
            }
            foreach (var go in gos) go?.GetComponent<CardLogicScript>().lgo_AttachedDon.Clear();
            foreach (var kv in m.Attached) gos[kv.Key].GetComponent<CardLogicScript>().lgo_AttachedDon.AddRange(kv.Value.Select(x => gos[x]));
            for (int i = 0; i < gos.Length; i++)
            {
                var piece = m.Pieces[i];
                var cls = gos[i].GetComponent<CardLogicScript>();
                if (!piece.don && piece.card != null) LoadDef(gos[i], piece.card);
                cls.myCard.bTapped = piece.rested;
                cls.myCard.bSummonSick = piece.sick;
                cls.myCard.bForcedFaceUp = piece.faceUp;
            }
            RefreshAll(true, instant);
        }

        // ---- controlli ----

        static void Problem(string what)
        {
            if (problems.Count < 50) problems.Add(what);
        }

        /// A fine turno: le liste del sim (carta per carta, in ordine), i DON attaccati e il riposo sono quelli del modello?
        static void Verify(int b)
        {
            checks++;
            var wrong = new List<string>();
            for (int p = 1; p <= 2; p++)
            {
                for (int z = 0; z < 9; z++)
                {
                    var sim = SimList(p, z, -1).Select(go => pieceOf.TryGetValue(go, out var i) ? i : -1).ToList();
                    if (!sim.SequenceEqual(model.Zones[p][z])) wrong.Add($"P{p} zona {z}: sim {sim.Count} carte, modello {model.Zones[p][z].Count}");
                }
                foreach (var host in model.Zones[p][8].Concat(model.Zones[p][2]))
                {
                    var sim = gos[host].GetComponent<CardLogicScript>().lgo_AttachedDon.Select(go => pieceOf[go]).ToList();
                    var want = model.Attached.TryGetValue(host, out var l) ? l : new List<int>();
                    if (!sim.SequenceEqual(want)) wrong.Add($"P{p} DON attaccati a {model.Pieces[host].card}: sim {sim.Count}, modello {want.Count}");
                }
                foreach (int z in new[] { 2, 5, 7, 8 })
                    foreach (var x in model.Zones[p][z])
                        if (gos[x].GetComponent<CardLogicScript>().myCard.bTapped != model.Pieces[x].rested) wrong.Add($"P{p} {model.Pieces[x].card}: riposo diverso");
                foreach (var x in model.Zones[p][1].Concat(model.Zones[p][2]).Concat(model.Zones[p][6]).Concat(model.Zones[p][7]))
                {
                    var def = gos[x].GetComponent<CardLogicScript>().myCard.cardDef;
                    if (model.Pieces[x].card != null && Known(model.Pieces[x].card) && (def == null || def.cardID != model.Pieces[x].card)) wrong.Add($"P{p} {model.Pieces[x].card}: carta diversa nel sim ({def?.cardID})");
                }
            }
            if (wrong.Count == 0) return;
            checksWrong++;
            Problem($"fine turno (battuta {b}, riga {log.Events[log.Beats[b].last].line}): " + string.Join("; ", wrong.Take(4)));
        }

        // ---- per il telecomando ----

        internal static string Status(bool full)
        {
            if (!Active) return "Replay Room chiusa";
            int line = done >= 0 ? log.Events[log.Beats[done].last].line : 0;
            int turn = log.Beats[Math.Min(log.Beats.Count - 1, done + 1)].turn;
            if (!full) return turn > 0 ? $"Turn {turn}" : "Setup";
            var sb = new StringBuilder();
            sb.AppendLine($"{Path.GetFileName(logPath)} · {(playing ? "play" : fast ? "veloce" : "pausa")} ×{speed.ToString(CultureInfo.InvariantCulture)} · battuta {done + 1}/{log.Beats.Count} · riga {line} di {log.Lines} · turno {turn}");
            sb.AppendLine($"controlli a fine turno: {checks} ({checksWrong} diversi) · mosse fuori posto nel sim: {misplaced} · avvisi del modello: {model.Warnings.Count} · CHK {model.ChkChecked} ({model.ChkWrong} diverse) · errori: {errors}");
            sb.AppendLine("suoni (partiti/in riproduzione): " + string.Join(", ", sounds.Select(kv => $"{kv.Key} {kv.Value.played}/{kv.Value.playing}")));
            foreach (var p in problems.Take(10)) sb.AppendLine("  " + p);
            foreach (var w in model.Warnings.Take(5)) sb.AppendLine("  modello: " + w);
            return sb.ToString().TrimEnd();
        }

        internal static bool Finished => Active && done >= log.Beats.Count - 1 && !fast;
        internal static bool Clean => checksWrong == 0 && misplaced == 0 && errors == 0 && model.Warnings.Count == 0 && model.ChkWrong == 0;
        internal static string Summary => $"{log.Beats.Count} battute, {checks} fine turno controllati ({checksWrong} diversi), {misplaced} fuori posto, {model.Warnings.Count} avvisi, CHK {model.ChkWrong}/{model.ChkChecked} diverse, {errors} errori"
            + (problems.Count > 0 ? " · " + problems[0] : model.Warnings.Count > 0 ? " · " + model.Warnings[0] : "");

        internal static void RunFast() { if (Active) { playing = false; fast = true; } }

        internal static string Command(string arg)
        {
            int sp = arg.IndexOf(' ');
            string sub = sp < 0 ? arg : arg.Substring(0, sp), rest = sp < 0 ? "" : arg.Substring(sp + 1).Trim();
            switch (sub)
            {
                case "play": if (!Active) return "chiusa"; if (!playing) Toggle(); return Status(true);
                case "pause": Pause(); return Status(true);
                case "step": Forward(); return Status(true);
                case "back": Back(); return Status(true);
                case "turn":
                    if (!Active) return "chiusa";
                    if (rest == "next" || rest == "+") { Turn(+1); return Status(true); }
                    if (rest == "prev" || rest == "-") { Turn(-1); return Status(true); }
                    return int.TryParse(rest, out var n) ? GoTurn(n) + "\n" + Status(true) : "turn <n>|next|prev";
                case "speed":
                    if (rest == "max") { RunFast(); return "a tutta velocità fino in fondo"; }
                    if (!float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return "speed <0.5|1|2|4|max>";
                    SetSpeed(s); return Status(true);
                case "hand":
                    if (rest == "all") SetHand(HandView.All);
                    else if (rest == "known") SetHand(HandView.Known);
                    else if (rest == "hide" || rest == "hidden" || rest == "off") SetHand(HandView.Hidden);
                    else ToggleHand();
                    return "mano dell'avversario: " + (handMode == HandView.All ? "tutte scoperte" : handMode == HandView.Known ? "solo carte note" : "coperta");
                case "stop": Exit(); return "chiusa";
                case "status": return Status(true);
                case "goto": if (!Active) return "chiusa"; JumpTo(int.Parse(rest) - 1); return Status(true);
                case "find":
                    // la prossima riga del log a sinistra che contiene il testo: ci si ferma subito prima (il passo dopo la fa)
                    if (!Active) return "chiusa";
                    for (int b = done + 1; b < log.Beats.Count; b++)
                        for (int i = log.Beats[b].first; i <= log.Beats[b].last; i++)
                            if (log.Events[i].kind == ReplayKind.Text && log.Events[i].shown && log.Events[i].text.IndexOf(rest, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                playing = false;
                                JumpTo(b - 1);
                                return $"prima di «{log.Events[i].text}» (riga {log.Events[i].line})\n" + Status(true);
                            }
                    return "non trovato: " + rest;
                case "rec": return Recorder.Command(rest);
                case "test": return Tests.Start(rest);
                case "share": ReplayStudy.ShareCurrentReplay(); return Active ? "sharing " + Path.GetFileName(logPath) : "chiusa";
                case "shot": if (!Active) return "chiusa"; ShareShot.Shoot(game); return "shot";
                case "openlink": ReplayLibrary.OpenShared(rest); return "opening shared " + rest;
                case "library": ReplayLibrary.Show(); return "library open";
                case "": return Status(true);
                default: return Open(arg.Trim().Trim('"'));
            }
        }

        internal static bool Over(Vector2 p) => Active && (ReplayLibrary.Visible || ReplayBar.Over(p) || ReplayStudy.Over(p));

        // ---- le prove dal telecomando: tutti i log di una cartella, uno dopo l'altro, a tutta velocità ----

        static class Tests
        {
            static Queue<string> queue;
            static readonly List<string> results = new List<string>();
            static int passed, total;
            static float nextAt;

            internal static string Start(string folder)
            {
                if (folder == "") return results.Count == 0 ? "nessuna prova (replay test <cartella>)" : $"{passed}/{total} puliti\n" + string.Join("\n", results);
                if (Active) return "prima replay stop";
                var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.log").OrderBy(f => f).ToArray() : new string[0];
                if (files.Length == 0) return "nessun log in " + folder;
                queue = new Queue<string>(files);
                results.Clear(); passed = total = 0;
                return $"prova di {files.Length} log: replay test (senza cartella) per vedere come va";
            }

            internal static void Tick()
            {
                if (queue == null || Time.realtimeSinceStartup < nextAt) return;
                if (Active)
                {
                    if (!Finished) return;
                    total++;
                    if (Clean) passed++;
                    results.Add($"{(Clean ? "ok" : "NO")} {Path.GetFileName(logPath)}: {Summary}");
                    Plugin.Log.LogInfo("replay test: " + results.Last());
                    Exit();
                    nextAt = Time.realtimeSinceStartup + 0.5f;
                    return;
                }
                if (queue.Count == 0) { queue = null; Plugin.Log.LogInfo($"replay test finito: {passed}/{total} puliti"); return; }
                var next = queue.Dequeue();
                var opened = Open(next);
                if (!Active) { total++; results.Add($"NO {Path.GetFileName(next)}: {opened}"); return; }
                RunFast();
            }
        }
    }

    // ---- la barra in basso a destra (dove in partita c'è End Turn): passo, play, turno, velocità, la mano coperta, esci ----

    static class ReplayBar
    {
        static GameObject bar, play, speedButton, drawOption, notesOption, hand, bottomOption, shareOption, options;
        static GameObject scrubTrack, noteDotsHolder;
        static RectTransform scrubFill, scrubThumb;
        static TextMeshProUGUI status;
        static Sprite dotSprite;
        static int drawnNoteVersion = int.MinValue;
        static readonly float[] Speeds = { 0.5f, 1, 2, 4 };

        sealed class ScrubHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public void OnPointerDown(PointerEventData e) => Seek(e);
            public void OnDrag(PointerEventData e) => Seek(e);
            void Seek(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                var cam = e.pressEventCamera;
                if (cam == null)
                {
                    var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
                    if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
                }
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, cam, out var local)) return;
                float t = Mathf.Clamp01((local.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
                int total = ReplayRoom.TotalBeats;
                int target = total > 0 ? Mathf.RoundToInt(t * total) - 1 : -1;
                ReplayRoom.Seek(target);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
        }

        static Sprite CircleDot()
        {
            if (dotSprite != null) return dotSprite;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float a = Mathf.Clamp01(size * 0.5f - 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply();
            dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
            return dotSprite;
        }

        internal static void Build(GameplayLogicScript game)
        {
            Destroy();
            var choice = (RectTransform)game.go_ChoiceButton1.transform;
            float bw = choice.rect.width, bh = choice.rect.height;
            bar = new GameObject("OPTCGTrainerReplayBar", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)bar.transform;
            rt.SetParent(choice.parent, false);
            rt.anchorMin = choice.anchorMin; rt.anchorMax = choice.anchorMax; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(bw * 1.08f, bh * 1.95f);
            rt.anchoredPosition = choice.anchoredPosition + new Vector2(0, bh * 0.40f);
            GameUi.Frame(bar, game.go_DownloadLog);
            GameUi.OnTop(bar);
            var le = bar.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            GameObject Button(string name, string label, Action click, float x, float y, float w, float h, Color tint, Transform parent = null)
            {
                var b = Look.CloneButton(game.go_DownloadLog, name, label, tint, () =>
                {
                    try { click(); } catch (Exception e) { Plugin.Log.LogError("Replay Room: " + e); }
                    // un pulsante selezionato riceverebbe anche lo spazio (play/pausa): niente selezione
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                });
                b.transform.SetParent(parent ?? rt, false);
                Look.Place((RectTransform)b.transform, x, y, w, h);
                var text = b.GetComponentInChildren<TMP_Text>(true);
                if (text != null) { text.enableAutoSizing = true; text.fontSizeMin = 8; text.fontSizeMax = bh * 0.3f; text.margin = Vector4.zero; text.alignment = TextAlignmentOptions.Center; }
                return b;
            }
            // Row 1: transport buttons
            float row1 = 0.06f, h1 = 0.33f, w = 0.148f, gap = 0.008f, x0 = 0.035f;
            Button("TurnBack", "|<", () => ReplayRoom.Turn(-1), x0, row1, w, h1, Look.Blue);
            Button("StepBack", "<", ReplayRoom.Back, x0 + (w + gap), row1, w, h1, Look.Blue);
            play = Button("Play", "Play", ReplayRoom.Toggle, x0 + 2 * (w + gap), row1, w, h1, Look.GoldTint);
            Button("Step", ">", ReplayRoom.Forward, x0 + 3 * (w + gap), row1, w, h1, Look.Blue);
            Button("TurnNext", ">|", () => ReplayRoom.Turn(+1), x0 + 4 * (w + gap), row1, w, h1, Look.Blue);
            speedButton = Button("Speed", "x1", NextSpeed, x0 + 5 * (w + gap), row1, w, h1, Look.Blue);

            // Row 2: timeline scrub bar with yellow note dots
            scrubTrack = new GameObject("ScrubTrack", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(ScrubHandler));
            scrubTrack.transform.SetParent(rt, false);
            Look.Place((RectTransform)scrubTrack.transform, 0.04f, 0.44f, 0.92f, 0.20f);
            scrubTrack.GetComponent<Image>().color = new Color32(0x14, 0x1d, 0x33, 0xff);
            var trackOutline = scrubTrack.GetComponent<Outline>();
            trackOutline.effectColor = CoachLook.CardLine;
            trackOutline.effectDistance = new Vector2(1f, -1f);

            var fillObj = new GameObject("ScrubFill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(scrubTrack.transform, false);
            scrubFill = (RectTransform)fillObj.transform;
            scrubFill.anchorMin = Vector2.zero; scrubFill.anchorMax = new Vector2(0f, 1f);
            scrubFill.offsetMin = scrubFill.offsetMax = Vector2.zero;
            var fillImg = fillObj.GetComponent<Image>();
            fillImg.color = CoachLook.Ready;
            fillImg.raycastTarget = false;

            var thumbObj = new GameObject("ScrubThumb", typeof(RectTransform), typeof(Image));
            thumbObj.transform.SetParent(scrubTrack.transform, false);
            scrubThumb = (RectTransform)thumbObj.transform;
            scrubThumb.anchorMin = new Vector2(0f, 0f); scrubThumb.anchorMax = new Vector2(0f, 1f);
            scrubThumb.pivot = new Vector2(0.5f, 0.5f);
            scrubThumb.anchoredPosition = Vector2.zero;
            scrubThumb.sizeDelta = new Vector2(6f, 4f);
            var thumbImg = thumbObj.GetComponent<Image>();
            thumbImg.color = CoachLook.Text;
            thumbImg.raycastTarget = false;

            noteDotsHolder = new GameObject("NoteDots", typeof(RectTransform));
            noteDotsHolder.transform.SetParent(scrubTrack.transform, false);
            var dotsRt = (RectTransform)noteDotsHolder.transform;
            dotsRt.anchorMin = Vector2.zero; dotsRt.anchorMax = Vector2.one;
            dotsRt.offsetMin = dotsRt.offsetMax = Vector2.zero;

            // Row 3: status, Options, Exit
            float row3 = 0.68f, h3 = 0.26f;
            var font = play.GetComponentInChildren<TMP_Text>(true);
            status = Look.NewText(rt, font, "Status", bh * 0.2f, TextAlignmentOptions.Left);
            Look.Place(status.rectTransform, 0.045f, row3, 0.49f, h3);
            status.enableAutoSizing = true; status.fontSizeMin = 8; status.fontSizeMax = bh * 0.22f;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            Button("Features", "Options", () => options.SetActive(!options.activeSelf), 0.55f, row3, 0.22f, h3, Look.Blue);
            Button("Exit", "Exit", ReplayRoom.Exit, 0.785f, row3, 0.18f, h3, Color.white);

            // Options popup menu (7 items)
            options = new GameObject("ReplayOptions", typeof(RectTransform), typeof(Image));
            options.transform.SetParent(rt, false);
            Look.Place((RectTransform)options.transform, 0.22f, -2.25f, 0.76f, 2.23f);
            options.GetComponent<Image>().color = CoachLook.Panel;
            GameObject Option(int row, string label, Action action, bool close = true) =>
                Button("ReplayOption" + row, label, () => { if (close) options.SetActive(false); action(); },
                    0.035f, 0.018f + row * 0.139f, 0.93f, 0.132f, Look.Blue, options.transform);
            drawOption = Option(0, "Draw", ReplayStudy.ToggleDraw);
            notesOption = Option(1, "Notes", ReplayStudy.ToggleNotes);
            hand = Option(2, ReplayRoom.HandButtonLabel, ReplayRoom.ToggleHand, close: false);
            bottomOption = Option(3, "Deck bottom", ReplayStudy.ToggleBottom);
            Option(4, "Screenshot", () => ShareShot.Shoot(game));
            shareOption = Option(5, "Share replay link", ReplayStudy.ShareCurrentReplay);
            Option(6, "Back to library", ReplayLibrary.Show);
            options.SetActive(false);

            RebuildNoteDots();
        }

        static void RebuildNoteDots()
        {
            if (noteDotsHolder == null) return;
            for (int i = noteDotsHolder.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(noteDotsHolder.transform.GetChild(i).gameObject);
            drawnNoteVersion = ReplayStudy.NoteVersion;
            int total = ReplayRoom.TotalBeats;
            if (total <= 0) return;
            var moments = ReplayStudy.NoteMoments();
            if (moments == null || moments.Count == 0) return;
            var sprite = CircleDot();
            foreach (var beat in moments)
            {
                float t = Mathf.Clamp01((beat + 1f) / total);
                var dot = new GameObject("NoteDot_" + beat, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
                dot.transform.SetParent(noteDotsHolder.transform, false);
                var drt = (RectTransform)dot.transform;
                drt.anchorMin = drt.anchorMax = new Vector2(t, 0.5f);
                drt.pivot = new Vector2(0.5f, 0.5f);
                drt.anchoredPosition = Vector2.zero;
                drt.sizeDelta = new Vector2(14f, 14f);
                var img = dot.GetComponent<Image>();
                img.sprite = sprite;
                img.color = CoachLook.Gold;
                var outline = dot.GetComponent<Outline>();
                outline.effectColor = new Color32(0x0d, 0x16, 0x30, 0xff);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                int targetBeat = beat;
                var btn = dot.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() =>
                {
                    try { ReplayStudy.JumpToNote(targetBeat); } catch (Exception e) { Plugin.Log.LogError("Replay Room: " + e); }
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                });
            }
        }

        static void NextSpeed()
        {
            int i = Array.IndexOf(Speeds, ReplayRoom.Speed);
            ReplayRoom.SetSpeed(Speeds[(i + 1) % Speeds.Length]);
        }

        internal static void Refresh(string text)
        {
            if (bar == null) return;
            int total = ReplayRoom.TotalBeats;
            string line = total > 0 ? $"{text} · {Mathf.Max(0, ReplayRoom.Moment + 1)}/{total}" : text;
            if (status.text != line) status.text = line;
            Look.SetLabel(play, ReplayRoom.Playing ? "Pause" : "Play");
            Look.SetLabel(speedButton, "x" + ReplayRoom.Speed.ToString(CultureInfo.InvariantCulture));
            float progress = total > 0 ? Mathf.Clamp01((ReplayRoom.Moment + 1f) / total) : 0f;
            if (scrubFill != null) scrubFill.anchorMax = new Vector2(progress, 1f);
            if (scrubThumb != null)
            {
                scrubThumb.anchorMin = new Vector2(progress, 0f);
                scrubThumb.anchorMax = new Vector2(progress, 1f);
            }
            if (drawnNoteVersion != ReplayStudy.NoteVersion) RebuildNoteDots();
            if (options.activeSelf)
            {
                Look.SetLabel(hand, ReplayRoom.HandButtonLabel);
                Look.SetTint(hand, ReplayRoom.RevealHand ? Look.GoldTint : Look.Blue);
                Look.SetTint(drawOption, ReplayStudy.DrawOpen ? Look.GoldTint : Look.Blue);
                Look.SetTint(notesOption, ReplayStudy.NotesOpen ? Look.GoldTint : Look.Blue);
                Look.SetTint(bottomOption, ReplayStudy.BottomOpen ? Look.GoldTint : Look.Blue);
            }
        }

        internal static void SetHiddenForShot(bool hide)
        {
            if (bar != null) bar.SetActive(!hide);
        }

        internal static bool Over(Vector2 p) => bar != null && (Look.Contains((RectTransform)bar.transform, p)
            || Look.Contains((RectTransform)options.transform, p));

        internal static void Destroy()
        {
            if (bar != null) UnityEngine.Object.Destroy(bar);
            bar = play = speedButton = drawOption = notesOption = hand = bottomOption = shareOption = options = scrubTrack = noteDotsHolder = null;
            scrubFill = scrubThumb = null;
            drawnNoteVersion = int.MinValue;
        }
    }

    // ---- i fotogrammi per il video, dall'interno di Unity (la finestra del laboratorio sta dietro alle altre) ----

    static class Recorder
    {
        static string folder;
        static int frame, every = 1, count, max;
        static bool on;

        internal static bool On => on;

        internal static string Command(string arg)
        {
            var parts = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return on ? $"registro: {count} fotogrammi in {folder}" : $"ferma: {count} fotogrammi in {folder}";
            if (parts[0] == "stop") { Stop(); return $"registrazione ferma: {count} fotogrammi in {folder}"; }
            // rec <cartella> [fotogrammi al secondo] [uno ogni n] [quanti al massimo]: il tempo del gioco va a passi fissi
            // (Time.captureFramerate), così il video ha il ritmo vero anche se salvare un fotogramma è lento
            // la cartella può avere spazi: i numeri sono le ultime parole
            var nums = new List<int>();
            int last = parts.Length;
            while (last > 1 && nums.Count < 3 && int.TryParse(parts[last - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { nums.Insert(0, n); last--; }
            folder = string.Join(" ", parts.Take(last)).Trim('"');
            int fps = nums.Count > 0 ? nums[0] : 30;
            every = nums.Count > 1 ? Math.Max(1, nums[1]) : 1;
            max = nums.Count > 2 ? nums[2] : int.MaxValue;
            Directory.CreateDirectory(folder);
            frame = count = 0;
            Time.captureFramerate = fps;
            if (!on) { on = true; Plugin.Instance.StartCoroutine(Loop()); }
            return $"registro in {folder}: {fps} fotogrammi al secondo di gioco, uno ogni {every}";
        }

        internal static void Stop()
        {
            on = false;
            Time.captureFramerate = 0;
        }

        static IEnumerator Loop()
        {
            var end = new WaitForEndOfFrame();
            while (on)
            {
                yield return end;
                if (!on) break;
                if (frame++ % every != 0) continue;
                try
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(folder, $"f{count++:00000}.jpg"), ImageConversion.EncodeToJPG(tex, 88));
                    UnityEngine.Object.Destroy(tex);
                    if (count >= max) { Stop(); ReplayRoom.Pause(); Plugin.Log.LogInfo($"registrazione finita: {count} fotogrammi in {folder}"); }
                }
                catch (Exception e) { Plugin.Log.LogError("fotogramma: " + e.Message); Stop(); }
            }
        }
    }

    // ---- gli agganci della Replay Room ----

    // la potenza e il costo che si vedono sulle carte e sulla freccia: quelli del log, non ricalcolati dagli effetti
    [HarmonyPatch(typeof(GameplayLogicScript), "CardPower")]
    static class ReplayPower
    {
        static bool Prefix(GameObject go_Card, bool bAttacking, bool bIgnoreDon, ref int __result)
        {
            try
            {
                int p = ReplayRoom.Power(go_Card, bAttacking, bIgnoreDon);
                if (p < 0) return true;
                __result = p;
                return false;
            }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "GetCardCost")]
    static class ReplayCost
    {
        static bool Prefix(GameObject go_Card, ref int __result)
        {
            try
            {
                int c = ReplayRoom.Cost(go_Card);
                if (c < 0) return true;
                __result = c;
                return false;
            }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "RefreshHandPositions")]
    static class ReplayHandFaces
    {
        static void Postfix() { try { ReplayRoom.FixFaces(); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "DrawAttackArrow")]
    static class ReplayArrow
    {
        static void Postfix() { try { ReplayRoom.FixArrow(); } catch (Exception) { } }
    }

    // Back to Main durante il replay: si esce dalla Replay Room (senza la riga "Quits!" e il messaggio di uscita nel Player.log)
    [HarmonyPatch(typeof(GameplayLogicScript), "ReturnToMain")]
    static class ReplayBackToMain
    {
        static bool Prefix()
        {
            if (!ReplayRoom.Active) return true;
            try { ReplayRoom.Exit(); } catch (Exception e) { Plugin.Log.LogError("Replay Room: " + e); }
            return false;
        }
    }

    // la scritta in basso a destra: in Observing il sim scrive "Spectating Match"; in partita, durante il turno
    // dell'avversario, "Opponent's Turn" (Guide.OpponentTurn) e niente durante il tuo. Come in partita
    [HarmonyPatch(typeof(GameplayLogicScript), "UpdateGuideText")]
    static class ReplayGuide
    {
        static void Postfix(GameplayLogicScript __instance)
        {
            if (!ReplayRoom.Active) return;
            try { __instance.text_GuideText.text = __instance.gsv_CurrentGame.iPlayerTurn == 1 ? __instance.tls_Translation.Translate("Guide.OpponentTurn") : ""; }
            catch (Exception) { }
        }
    }

    // mentre si registra il video il mouse non conta: il sim leggerebbe il puntatore vero di chi usa il PC, anche con la
    // finestra dietro alle altre, e aprirebbe la carta grande a caso
    [HarmonyPatch(typeof(GameplayLogicScript), "HandleMouseHover")]
    static class ReplayNoHoverWhileRecording
    {
        static void Postfix(GameplayLogicScript __instance)
        {
            var card = __instance.go_FocusedObject;
            if (!ReplayRoom.Active || card == null || __instance.Lps_Players.Count < 2) return;
            if (__instance.Lps_Players[1].Lgo_MyHand.Contains(card) && !card.GetComponent<CardLogicScript>().myCard.bFaceUp)
                __instance.go_FocusedObject = null;
        }
        static bool Prefix(GameplayLogicScript __instance)
        {
            if (!Recorder.On) return true;
            __instance.go_FocusedObject = null;
            return false;
        }
    }

    // mentre si registra il video ogni fotogramma si disegna (il sim, a carte ferme, ne disegna uno ogni 4)
    [HarmonyPatch(typeof(GameplayLogicScript), "UpdateRenderRate")]
    static class ReplayRenderRate
    {
        static void Postfix() { if (Recorder.On) OnDemandRendering.renderFrameInterval = 1; }
    }

    /// La guardia: finché la Replay Room è aperta questi metodi del sim non fanno niente. Sono i posti da cui il sim scrive
    /// il log della partita (CombatLogs, il registro CDK, il Player.log con le righe [ReplaySync]), manda la partita ai suoi
    /// server, la chiude (vittoria, concede) o legge i tasti (H riordinerebbe la mano). Si attaccano uno per uno: se uno manca
    /// (versione nuova del sim) la Replay Room non si apre, il resto della mod va avanti.
    static class ReplayGuard
    {
        static readonly (Type type, string name)[] Blocked =
        {
            (typeof(GameplayLogicScript), "ReplaySync_AppendGameLine"),
            (typeof(GameplayLogicScript), "ReplaySync_AppendLineOnly"),
            (typeof(GameplayLogicScript), "ReplaySync_AfterMutation"),
            (typeof(GameplayLogicScript), "ReplaySync_EmitMove"),
            (typeof(GameplayLogicScript), "ReplaySync_EmitCheck"),
            (typeof(GameplayLogicScript), "ReplaySync_FlushFieldState"),
            (typeof(GameplayLogicScript), "ReplaySync_Semantic"),
            (typeof(GameplayLogicScript), "ReplaySync_Seal"),
            (typeof(GameplayLogicScript), "ReplaySync_SaveJournal"),
            (typeof(GameplayLogicScript), "ReplaySync_JournalTick"),
            (typeof(GameplayLogicScript), "CDKReplay_Append"),
            (typeof(GameplayLogicScript), "CDKReplay_AfterMutation"),
            (typeof(GameplayLogicScript), "CDKReplay_CaptureLogLine"),
            (typeof(GameplayLogicScript), "SaveMyLogLines"),
            (typeof(GameplayLogicScript), "LogMatchToAPI"),
            (typeof(GameplayLogicScript), "RecordWinAnalytic"),
            (typeof(GameplayLogicScript), "RecordDiscconnectWinAnalytic"),
            (typeof(GameplayLogicScript), "BatsuLogs"),
            (typeof(GameplayLogicScript), "EndGame"),
            (typeof(GameplayLogicScript), "ConcedeLocal"),
            (typeof(GameplayLogicScript), "Concede"),
            (typeof(GameplayLogicScript), "HandleKeyboardAction"),
            (typeof(ReplayEvidenceJournal), "QueueUpload"),
            (typeof(ReplayEvidenceJournal), "RecordSettlement"),
            (typeof(ReplayEvidenceJournal), "RetryUploads"),
        };

        internal static bool Ready { get; private set; }
        internal static string Missing { get; private set; } = "non attaccata";

        internal static void Install(Harmony harmony)
        {
            var missing = new List<string>();
            var block = new HarmonyMethod(AccessTools.Method(typeof(ReplayGuard), nameof(Block)));
            var blockTask = new HarmonyMethod(AccessTools.Method(typeof(ReplayGuard), nameof(BlockTask)));
            foreach (var (type, name) in Blocked)
            {
                try
                {
                    var m = AccessTools.Method(type, name);
                    if (m == null) { missing.Add(type.Name + "." + name); continue; }
                    harmony.Patch(m, prefix: typeof(Task).IsAssignableFrom(m.ReturnType) ? blockTask : block);
                }
                catch (Exception e) { missing.Add(type.Name + "." + name + " (" + e.Message + ")"); }
            }
            Ready = missing.Count == 0;
            Missing = string.Join(", ", missing);
            if (!Ready) Plugin.Log.LogError("Replay Room spenta, guardia incompleta: " + Missing);
        }

        static bool Block() => !ReplayRoom.Active;

        static bool BlockTask(ref Task __result)
        {
            if (!ReplayRoom.Active) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }
}
