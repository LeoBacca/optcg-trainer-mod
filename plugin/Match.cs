// La partita in corso, vista dalla mod: a che riga è il log del sim, che turno è, le note scritte giocando,
// e cosa fare quando finisce (copia del log, caricamento sul sito). Legge soltanto: il log del sim non si tocca mai
// (le sue righe RZ1 sono legate da una catena di hash, la prova della partita per OPBounty).
//
// Quando il sim chiude un log:
//   chiuso bene (riga RZ1|END)          vittoria, sconfitta, concede        → partita "finita", si carica sempre
//   buttato via a metà                  Back to Main, partita nuova, sim chiuso → "abbandonata", si carica se era
//                                       cominciata davvero (turno ≥ 1) o se ha delle note
//   buttato via da "Restart this turn"  (e dalle frecce dei turni) il sim riparte con un log nuovo dallo stesso punto
//                                       → il pezzo di prima si carica solo se ha delle note, per non riempire il sito
using System;
using System.IO;
using HarmonyLib;

namespace OPTCGTrainer
{
    static class Match
    {
        static readonly AccessTools.FieldRef<GameplayLogicScript, ReplayEvidenceJournal> JournalRef =
            AccessTools.FieldRefAccess<GameplayLogicScript, ReplayEvidenceJournal>("_replayJournal");
        static readonly AccessTools.FieldRef<GameplayLogicScript, bool> SealedRef =
            AccessTools.FieldRefAccess<GameplayLogicScript, bool>("_replaySealed");

        /// La partita aperta adesso (o l'ultima finita, finché non ne comincia un'altra). null prima della prima riga del log.
        internal static MatchFile Current;
        /// Sale a ogni partita nuova: la UI la confronta per rimettersi a posto.
        internal static int Generation;
        /// Il sim sta rifacendo un turno (GameStateStepTurnDirection): il log che butta via è solo un pezzo di partita.
        internal static bool Restarting;

        static ReplayEvidenceJournal Journal(GameplayLogicScript game) => game != null ? JournalRef(game) : null;

        /// La partita della mod per il log che il sim sta scrivendo adesso (la crea alla prima richiesta).
        internal static MatchFile ForGame(GameplayLogicScript game)
        {
            var journal = Journal(game);
            if (journal == null) return Current != null && Current.Finished ? Current : null;
            if (Current == null || Current.id != journal.Id)
            {
                Current = Store.LoadMatch(journal.Id);
                Current.logName = Path.GetFileName(journal.Path);
            }
            return Current;
        }

        /// Il punto della partita dove ancorare una nota: righe scritte finora (da 1) e turno.
        internal static (int line, int turn) Now(GameplayLogicScript game)
        {
            int line = Math.Max(1, game.currentCombatLog?.Count ?? 1);
            int turn = game.gsv_CurrentGame != null ? game.gsv_CurrentGame.iTurnNumber : 0;
            return (line, turn);
        }

        /// A ogni fotogramma (GameUi.Tick): il turno più alto visto, per sapere se una partita abbandonata era cominciata.
        /// Il sim azzera il turno prima di buttare via il log, quindi va ricordato prima.
        internal static void Track(GameplayLogicScript game)
        {
            if (game.gsv_CurrentGame == null || Journal(game) == null) return;
            var m = ForGame(game);
            if (m == null || m.Finished) return;
            if (game.gsv_CurrentGame.iTurnNumber > m.maxTurn) m.maxTurn = game.gsv_CurrentGame.iTurnNumber;
            Describe(game, m);
        }

        /// Il tipo di partita, come lo sa il sim: Solo v Self; ladder se è una partita OPBounty (ha il suo numero, che il sim
        /// scrive anche nel log, riga RZ1|META); lobby privata con codice; altrimenti la coda pubblica del formato scelto.
        /// Si ricontrolla finché la partita non finisce: il numero OPBounty può arrivare qualche istante dopo l'inizio.
        static void Describe(GameplayLogicScript game, MatchFile m)
        {
            int ladder = OPBMatchManager.ActiveServerMatchNo;
            string kind = game.e_GameStyle == GameStyle.SoloVSelf ? "solo" : ladder > 0 ? "ladder" : game.isPrivate ? "private" : "queue";
            if (kind == m.matchKind && (kind != "ladder" || m.ladderMatch == ladder)) return;
            m.matchKind = kind;
            m.ladderMatch = kind == "ladder" ? ladder : 0;
            m.matchFormat = kind == "solo" || game.eMultiStyle == MultiplayerStyle.Private ? null : FormatName(game);
            Plugin.Log.LogInfo($"tipo di partita: {m.matchKind}{(m.matchFormat != null ? " · " + m.matchFormat : "")}{(m.ladderMatch > 0 ? " · OPBounty " + m.ladderMatch : "")}");
        }

        // il nome della coda come lo vede il giocatore (dalle traduzioni del sim: Button.Nationals = "OP18", Button.Western = "Standard"…)
        internal static string FormatName(GameplayLogicScript game, MultiplayerStyle? style = null)
        {
            var key = "Button." + (style ?? game.eMultiStyle);
            string name = null;
            try { name = game.tls_Translation != null ? game.tls_Translation.Translate(key) : null; } catch (Exception) { }
            if (string.IsNullOrEmpty(name) || name == "UNTRANSLATED") name = (style ?? game.eMultiStyle).ToString();
            name = name.Replace("<br>", " ").Trim();
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        internal static void AddNote(GameplayLogicScript game, int line, int turn, string text)
        {
            var m = ForGame(game);
            if (m == null || string.IsNullOrWhiteSpace(text)) return;
            m.notes.Add(new NoteItem { line = line, turn = turn, text = text.Trim(), createdAt = DateTime.UtcNow.ToString("o") });
            Store.SaveMatch(m); // subito: se il sim si chiude, la nota c'è
            Plugin.Log.LogInfo($"nota salvata (riga {line}, turno {turn}) per {m.id}");
            if (m.Finished) Account.Kick(); // nota scritta a partita finita: va caricata anche lei
        }

        // il sim ha appena salvato il log: se la partita è chiusa (riga RZ1|END scritta) è finita
        internal static void AfterSave(GameplayLogicScript game)
        {
            var journal = Journal(game);
            // il file diventa .log quando il sim lo chiude; se la rinomina non riesce resta .partial.log, ma la partita è finita lo stesso
            if (journal != null && SealedRef(game)) Register(game, journal, "finita");
        }

        /// Il sim sta per buttare via il log senza la riga di fine: la partita si registra lo stesso, con il suo .partial.log
        /// (il sito lo accetta, come quelli della cartella AutoSaved), ma solo se vale la pena (vedi in cima).
        internal static void Abandon(GameplayLogicScript game)
        {
            var journal = Journal(game);
            if (journal == null) return;
            if (SealedRef(game)) { AfterSave(game); return; }
            var m = ForGame(game);
            if (m == null || m.Finished) return;
            bool worth = m.notes.Count > 0 || (!Restarting && m.maxTurn >= 1);
            if (!worth) return; // un log di 2 righe ("Quits!" nel menu), un mulligan lasciato lì, un pezzo di turno rifatto senza note
            try { journal.Checkpoint(); } catch (Exception) { /* già chiuso: il file sul disco è quello che c'è */ }
            Register(game, journal, Restarting ? "spezzata da un turno rifatto" : "abbandonata");
        }

        static void Register(GameplayLogicScript game, ReplayEvidenceJournal journal, string how)
        {
            var m = ForGame(game);
            if (m == null || m.Finished) return;
            var source = Path.GetFullPath(journal.Path); // relativo alla cartella del sim, che è la cartella di lavoro
            var backup = Path.Combine(Store.Logs, Path.GetFileName(source));
            try
            {
                // il sim può tenere ancora il file aperto in scrittura: si legge in condivisione
                using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var to = new FileStream(backup, FileMode.Create, FileAccess.Write))
                    from.CopyTo(to);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("copia del log non riuscita: " + e.Message);
                return;
            }
            m.logName = Path.GetFileName(source);
            m.backup = backup;
            m.finishedAt = DateTime.UtcNow.ToString("o");
            Store.SaveMatch(m);
            Plugin.Log.LogInfo($"partita {how}: {m.logName}, {m.matchKind ?? "tipo ?"}{(m.matchFormat != null ? " · " + m.matchFormat : "")}, {m.notes.Count} note, copia in {backup}");
            Account.Kick();
        }

        /// All'avvio: le partite con note rimaste a metà (sim crashato) si cercano nella cartella dei log del sim e si registrano.
        internal static void RecoverLeftovers()
        {
            var dir = Path.GetFullPath(Path.Combine("CombatLogs", "AutoSaved"));
            foreach (var m in Store.Unfinished())
            {
                var partial = Path.Combine(dir, m.logName);
                var closed = Path.Combine(dir, m.logName.Replace(".partial.log", ".log"));
                var source = File.Exists(closed) ? closed : partial;
                if (!File.Exists(source)) continue; // cancellato (es. aggiornamento del sim): le note restano nel file della mod
                var backup = Path.Combine(Store.Logs, Path.GetFileName(source));
                try { File.Copy(source, backup, true); }
                catch (Exception e) { Plugin.Log.LogWarning("recupero di " + m.logName + ": " + e.Message); continue; }
                m.logName = Path.GetFileName(source);
                m.backup = backup;
                m.finishedAt = DateTime.UtcNow.ToString("o");
                Store.SaveMatch(m);
                Plugin.Log.LogInfo($"partita recuperata: {m.logName}, {m.notes.Count} note");
            }
        }

        internal static void OnReset()
        {
            Current = null;
            Generation++;
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "SaveMyLogLines")]
    static class AfterSaveLog
    {
        static void Postfix(GameplayLogicScript __instance)
        {
            try { Match.AfterSave(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("fine partita: " + e.Message); }
        }
    }

    // il sim butta via il log (partita nuova, rivincita, Back to Main, turno rifatto): prima si registra quello che c'era
    [HarmonyPatch(typeof(GameplayLogicScript), "ReplaySync_Reset")]
    static class OnLogReset
    {
        static void Prefix(GameplayLogicScript __instance)
        {
            try
            {
                Match.Abandon(__instance);
                Match.OnReset();
            }
            catch (Exception e) { Plugin.Log.LogWarning("partita nuova: " + e.Message); }
        }
    }

    // "Restart this turn" e le frecce dei turni: il sim ricarica il turno e ricomincia un log nuovo
    [HarmonyPatch(typeof(GameplayLogicScript), "GameStateStepTurnDirection")]
    static class OnTurnRestart
    {
        static void Prefix() => Match.Restarting = true;
        static Exception Finalizer(Exception __exception)
        {
            Match.Restarting = false; // anche se il sim va in errore; l'errore resta suo
            return __exception;
        }
    }

    // si chiude il sim (o la scena): una partita a metà si registra prima che il sim butti via il suo log
    [HarmonyPatch(typeof(GameplayLogicScript), "OnDestroy")]
    static class OnGameDestroyed
    {
        static void Prefix(GameplayLogicScript __instance)
        {
            try { Match.Abandon(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("chiusura della partita: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "OnApplicationQuit")]
    static class OnSimQuit
    {
        static void Prefix(GameplayLogicScript __instance)
        {
            try { Match.Abandon(__instance); } // si carica al prossimo avvio
            catch (Exception e) { Plugin.Log.LogWarning("chiusura del sim: " + e.Message); }
        }
    }
}
