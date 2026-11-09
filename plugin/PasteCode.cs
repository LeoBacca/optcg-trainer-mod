// L'incolla del codice di OPBounty (richiesta di Leo). Con il programma OPBounty (Play Ranked / Launch OPBounty) chi entra
// nella stanza riceve il codice negli appunti ("PLEASE PASTE LOBBY CODE"): la mod lo mette da sola nel campo della
// schermata Private (quella che si apre con Multiplayer). Join lo preme il giocatore: un codice vecchio porterebbe nella
// stanza sbagliata. La mod legge solo gli appunti, come fa OPBounty: non tocca la rete né OPBounty.
// Impostazione Multiplayer.PasteOPBountyCode. Nel log niente codice e nessuna frase che OPBounty legge (tools/pacchetto.ps1).
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace OPTCGTrainer
{
    static class PasteCode
    {
        // 7 caratteri, la prima lettera è il formato della stanza (X = OP18, ...)
        static readonly Regex RoomCode = new Regex("^[WEUXKABP][A-Z0-9]{6}$");
        static readonly FieldInfo MyRoomId = AccessTools.Field(typeof(HostJoinScript), "roomID");
        const float Lifetime = 300f;   // dopo 5 minuti la stanza di OPBounty non c'è più: il codice non si mette

        static string seen, pending;
        static float pendingSince, next;
        static HostJoinScript host;

        internal static void Tick()
        {
            if (!Plugin.PasteOPBountyCode.Value || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;

            var clip = (Clipboard() ?? "").Trim();
            if (seen == null) { seen = clip; return; }   // quello che c'era già negli appunti all'avvio è vecchio
            if (clip != seen)
            {
                seen = clip;
                var code = clip.ToUpperInvariant();
                if (RoomCode.IsMatch(code)) { pending = code; pendingSince = Time.realtimeSinceStartup; }
            }
            if (pending == null) return;
            if (Time.realtimeSinceStartup - pendingSince > Lifetime) { pending = null; return; }

            if (host == null) host = Object.FindAnyObjectByType<HostJoinScript>();
            if (host == null || host.go_IPAddress == null || host.tmp_IPAddress == null) return;
            // il codice della stanza creata da me (Copy Room ID, da mandare a un amico) non va nel campo per entrare
            if (pending == host.joinCode || pending == MyRoomId?.GetValue(host) as string) { pending = null; return; }
            // si aspetta che il giocatore apra la schermata Private (il codice resta pronto per 5 minuti)
            if (!host.go_IPAddress.activeInHierarchy) return;

            host.tmp_IPAddress.text = pending;
            pending = null;
            Plugin.Log.LogInfo("codice di OPBounty messo nel campo della schermata Private");
        }

        static string Clipboard()
        {
#if DEVTOOLS
            // nelle prove gli appunti sono un file: le prove non toccano mai gli appunti veri di chi usa il PC
            var fake = Path.Combine(Paths.ConfigPath, "optcgtrainer-clipboard.txt");
            if (DevCommands.Enabled) return File.Exists(fake) ? File.ReadAllText(fake) : "";
#endif
            return GUIUtility.systemCopyBuffer;
        }
    }
}
