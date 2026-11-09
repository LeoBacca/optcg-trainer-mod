// La schermata Multiplayer senza le cose che non servono (richiesta di Leo): via il banner degli sponsor che scorre
// con i suoi 3 tasti sotto, il pulsante «TCG Match Making / Discord» e «Match History». Resta «Play Ranked / Launch
// OPBounty» e tutto il resto (Select Deck, formati, Host, codice, Join, Back). Impostazione Multiplayer.HideExtras.
// Batsu li accende e spegne tutti insieme in HostJoinScript.SetMulti (anche nelle lobby pubbliche). Non si spengono:
// si nascondono con una CanvasGroup (trasparenti e non cliccabili), che resta attaccata all'oggetto anche quando lui
// li riaccende, così non c'è nemmeno un fotogramma con il banner. Ogni mezzo secondo si ricontrolla lo stesso
// (se un giorno Batsu ci mette una CanvasGroup sua, o il menu rinasce). Se un oggetto non c'è, non si fa niente.
// Il sim continua a scaricare gli sponsor e a farli scorrere come sempre: la mod non tocca la rete né i suoi file.
// Via anche «Open OPBounty» in alto a destra (il pannello OPBounty dentro il sim: Leo usa Play Ranked). Solo quel pulsante
// apre il pannello (OPBManager.Toggle), quindi nascosto lui il pannello non si apre mai.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class CleanMultiplayer
    {
        // i campi di HostJoinScript da nascondere (go_OPBounty, cioè Play Ranked, resta): nel sim 1.45a sono gli oggetti
        // Sponsors (il banner), SponsorTabLeft / SponsorTab / SponsorTabRight (i 3 tasti), TCGMMSponsor, MatchHistory
        static readonly FieldInfo[] Hides = new[] { "go_Sponsor", "go_SponsorButton1", "go_SponsorButton2", "go_SponsorButton3", "go_Sponsor2", "go_MatchHistory" }
            .Select(name => AccessTools.Field(typeof(HostJoinScript), name)).Where(f => f != null).ToArray();

        static HostJoinScript host;
        static OPBManager opBounty;
        static GameObject opBountyToggle;
        static float nextCheck;
        static readonly Dictionary<GameObject, (float alpha, bool blocks, bool interactable, bool added)> hidden =
            new Dictionary<GameObject, (float, bool, bool, bool)>();

        internal static void Tick()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 0.5f;
            bool on = Plugin.HideMultiplayerExtras.Value;
            if (host == null && opBounty == null) hidden.Clear(); // erano oggetti di un menu che non c'è più
            if (!on && hidden.Count == 0) return;
            if (host == null)
            {
                host = Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                if (host != null) Plugin.Log.LogInfo("Multiplayer, nascosti: " + string.Join(", ", Hides.Select(f => f.GetValue(host) as GameObject).Where(go => go != null).Select(go => go.name)));
            }
            if (host != null)
                foreach (var field in Hides)
                    if (field.GetValue(host) is GameObject go && go != null) SetHidden(go, on);

            if (opBounty == null)
            {
                opBounty = Object.FindAnyObjectByType<OPBManager>(FindObjectsInactive.Include);
                var button = opBounty != null && opBounty.toggleButtonText != null ? opBounty.toggleButtonText.GetComponentInParent<Button>(true) : null;
                opBountyToggle = button != null ? button.gameObject : null;
            }
            if (opBountyToggle != null) SetHidden(opBountyToggle, on);
        }

        static void SetHidden(GameObject go, bool hide)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (hide)
            {
                // la CanvasGroup di Batsu, se c'è, si ricorda com'era; se no se ne mette una nostra (e si toglie dopo)
                if (group == null) { group = go.AddComponent<CanvasGroup>(); hidden[go] = (1f, true, true, true); }
                else if (!hidden.ContainsKey(go)) hidden[go] = (group.alpha, group.blocksRaycasts, group.interactable, false);
                group.alpha = 0;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            else if (hidden.TryGetValue(go, out var was))
            {
                hidden.Remove(go);
                if (group == null) return;
                if (was.added) Object.Destroy(group);
                else { group.alpha = was.alpha; group.blocksRaycasts = was.blocks; group.interactable = was.interactable; }
            }
        }
    }
}
