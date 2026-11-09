// Il pulsante OPTCG TRAINER nel menu principale del sim: una copia del pulsante "Patch Notes" di Batsu, blu, con il logo
// grande che esce dal bordo sinistro (versione A, scelta da Leo); sta a specchio di Patch Notes, sopra "Deck Editor".
// Un clic apre il sito; se il sim non è ancora collegato all'account, fa partire il collegamento (le pagine del sito dicono
// di usare questo pulsante). Sotto, una riga piccola dice lo stato. Se il pulsante di Batsu non si trova, non compare.
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class MenuButton
    {
        internal static GameObject Instance;
        static GameObject source;
        static TextMeshProUGUI status;
        static Button statusButton;

        // HostJoinScript.Start traduce le scritte del menu ("Button.Patchnotes" → "Patch Notes") qualche istante dopo
        // il caricamento: si copia solo dopo, altrimenti la copia nascerebbe con la chiave al posto della scritta.
        internal static IEnumerator AddWhenReady()
        {
            for (int i = 0; i < 80 && Instance == null; i++)
            {
                var host = Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                var patchNotes = host != null ? host.go_Patchnotes : null;
                if (patchNotes != null && (Translated(patchNotes) || i >= 60)) Add(patchNotes);
                yield return new WaitForSeconds(0.25f);
            }
        }

        // Batsu accende e spegne i pulsanti del menu uno per uno (HostJoinScript.SetMain): il nostro segue Patch Notes
        internal static void FollowSource()
        {
            if (Instance == null || source == null) return;
            if (Instance.activeSelf != source.activeSelf) Instance.SetActive(source.activeSelf);
            if (status == null || !Instance.activeInHierarchy) return;
            var text = Account.Linking ? $"Confirm on the website: code <b>{Account.LinkCode}</b>"
                : Updater.Text != null ? Updater.Text
                : Account.Linked ? "Linked as <noparse>" + (Account.Nickname ?? "").Replace("</noparse>", "") + "</noparse>"
                : Account.LastError ?? "Click to link your OPTCG Trainer account";
            if (status.text != text) status.text = text;
            // la riga si clicca solo quando offre l'aggiornamento (o di riprovarlo); se no i clic passano come prima
            bool clickable = !Account.Linking && Updater.Clickable;
            if (status.raycastTarget != clickable) status.raycastTarget = statusButton.enabled = clickable;
        }

        static void Click()
        {
            if (Account.Linked) Account.Open(Account.Site);
            else Account.StartLink();
        }

        static bool Translated(GameObject go)
        {
            var text = go.GetComponentInChildren<TMP_Text>(true);
            return text != null && !text.text.StartsWith("Button.");
        }

        static void Add(GameObject patchNotes)
        {
            var label = $"<size=118%>OPTCG</size>\n<size=78%><cspace=0.16em><color={Look.GoldHex}>TRAINER</color></cspace></size>";
            var copy = Look.CloneButton(patchNotes, "OPTCGTrainerButton", label, Look.Blue, Click);
            copy.transform.SetSiblingIndex(patchNotes.transform.GetSiblingIndex() + 1);

            // a specchio rispetto al centro dello schermo: Patch Notes sta a destra, noi a sinistra
            var canvas = (RectTransform)patchNotes.GetComponentInParent<Canvas>().rootCanvas.transform;
            var center = canvas.TransformPoint(canvas.rect.center);
            var p = patchNotes.transform.position;
            copy.transform.position = new Vector3(2 * center.x - p.x, p.y, p.z);
            Look.AddLogoOut(copy);

            // la riga dello stato, sopra il pulsante dove c'è spazio (dentro la copia: compare e sparisce con lui)
            var rect = (RectTransform)copy.transform;
            var font = copy.GetComponentInChildren<TMP_Text>(true);
            status = Look.NewText(rect, font, "Status", rect.rect.height * 0.2f, TextAlignmentOptions.Bottom);
            var srt = status.rectTransform;
            srt.anchorMin = new Vector2(0.05f, 1); srt.anchorMax = new Vector2(1.15f, 1); srt.pivot = new Vector2(0.5f, 0);
            srt.anchoredPosition = new Vector2(0, rect.rect.height * 0.08f);
            srt.sizeDelta = new Vector2(0, rect.rect.height * 0.45f);
            status.enableAutoSizing = true;
            status.fontSizeMin = 8;
            status.fontSizeMax = rect.rect.height * 0.2f;
            status.outlineWidth = 0.18f;
            status.outlineColor = new Color32(10, 20, 45, 255);
            statusButton = status.gameObject.AddComponent<Button>();
            statusButton.transition = Selectable.Transition.None;
            statusButton.onClick.AddListener(Updater.Install);
            statusButton.enabled = false;

            Instance = copy;
            source = patchNotes;
            Plugin.Log.LogInfo("pulsante OPTCG TRAINER aggiunto al menu");
        }
    }
}
