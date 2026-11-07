// Lo stile della mod, in un posto solo: copie dei pulsanti di Batsu (così sembrano del sim), il blu, il logo,
// le scritte bianche. I pulsanti si copiano sempre ACCANTO all'originale, mai dentro: Batsu cerca le scritte
// dentro i suoi oggetti per tradurle (vedi HostJoinScript.TranslateButtonObject).
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class Look
    {
        // i pulsanti di Batsu sono color pergamena: il colore si moltiplica su quel colore, per questo i valori sono così accesi
        internal static readonly Color Blue = new Color(0.25f, 0.52f, 1f);
        internal static readonly Color GoldTint = new Color(1f, 0.86f, 0.42f);
        internal const string GoldHex = "#F5C242";
        internal static readonly Color PanelTint = new Color(0.16f, 0.24f, 0.5f);

        /// Una copia di un pulsante di Batsu: stessa forma e font, colore e scritta nostri, il clic che diciamo noi.
        internal static GameObject CloneButton(GameObject template, string name, string label, Color tint, UnityAction onClick)
        {
            var copy = SafeCopy(template, template.transform.parent);
            copy.name = name;
            var button = copy.GetComponentInChildren<Button>(true);
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                if (onClick != null) button.onClick.AddListener(onClick);
                button.interactable = true;
                if (button.targetGraphic != null) button.targetGraphic.color = tint;
                if (button.GetComponent<HoverGrow>() == null) button.gameObject.AddComponent<HoverGrow>();
            }
            var text = copy.GetComponentInChildren<TMP_Text>(true);
            if (text != null) WhiteLabel(text, label);
            return copy;
        }

        /// Copia un oggetto di Batsu senza far partire i suoi script: la copia nasce dentro un contenitore spento
        /// (lì Unity non chiama Awake/OnEnable), si ripulisce e solo dopo va al suo posto. L'originale non si tocca.
        internal static GameObject SafeCopy(GameObject template, Transform parent)
        {
            // il contenitore copre esattamente il genitore: la copia ha le stesse misure e la stessa posizione dell'originale
            var holder = new GameObject("OPTCGTrainerHolder", typeof(RectTransform));
            holder.SetActive(false);
            var hrt = (RectTransform)holder.transform;
            hrt.SetParent(parent, false);
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            var copy = Object.Instantiate(template, holder.transform);
            StripBatsuScripts(copy);
            copy.SetActive(true);
            copy.transform.SetParent(parent, false);
            Object.Destroy(holder);
            return copy;
        }

        /// Via dalla copia ogni script di Batsu (scelte, tooltip, suoni) e le trasparenze: restano solo forma e scritta.
        internal static void StripBatsuScripts(GameObject copy)
        {
            var batsu = typeof(GameplayLogicScript).Assembly;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Assembly == batsu) Object.DestroyImmediate(mb);
            foreach (var trigger in copy.GetComponentsInChildren<EventTrigger>(true)) Object.DestroyImmediate(trigger);
            foreach (var group in copy.GetComponentsInChildren<CanvasGroup>(true)) Object.DestroyImmediate(group);
        }

        /// Scritta bianca in grassetto, su un materiale tutto suo (gli altri pulsanti di Batsu non cambiano).
        internal static void WhiteLabel(TMP_Text text, string label)
        {
            text.richText = true;
            text.text = label;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle |= FontStyles.Bold;
            text.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            text.color = Color.white;
        }

        internal static void SetLabel(GameObject button, string label)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.text != label) text.text = label;
        }

        internal static void SetTint(GameObject button, Color tint)
        {
            var b = button.GetComponentInChildren<Button>(true);
            if (b != null && b.targetGraphic != null) b.targetGraphic.color = tint;
        }

        /// Il logo grande, per metà fuori dal bordo sinistro del pulsante, e la scritta spostata a destra per fargli posto.
        internal static void AddLogoOut(GameObject buttonRoot, float sizeFactor = 1.45f)
        {
            var button = buttonRoot.GetComponentInChildren<Button>(true);
            var label = buttonRoot.GetComponentInChildren<TMP_Text>(true);
            if (button == null || Plugin.LogoSprite == null) return;
            var rect = (RectTransform)button.transform;
            float h = rect.rect.height, size = h * sizeFactor;
            var logo = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            var logoRect = (RectTransform)logo.transform;
            logoRect.SetParent(rect, false);
            logoRect.anchorMin = logoRect.anchorMax = new Vector2(0, 0.5f);
            logoRect.sizeDelta = new Vector2(size, size);
            logoRect.anchoredPosition = new Vector2(size * 0.18f, h * 0.04f);
            var image = logo.GetComponent<Image>();
            image.sprite = Plugin.LogoSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (label != null)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = 8;
                label.fontSizeMax = 200;
                label.alignment = TextAlignmentOptions.Center;
                label.lineSpacing = -18;
                label.margin = new Vector4(size * 0.66f, h * 0.08f, h * 0.14f, h * 0.08f);
            }
        }

        /// Una scritta nuova con il font di Batsu (preso da una sua scritta).
        internal static TextMeshProUGUI NewText(Transform parent, TMP_Text fontFrom, string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (fontFrom != null) text.font = fontFrom.font;
            // il materiale del font di Batsu ha la faccia scura: su uno tutto nostro la mettiamo bianca, e il colore decide
            text.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            text.fontSize = size;
            text.alignment = align;
            text.color = Color.white;
            text.richText = true;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        /// Mette un rettangolo dentro il genitore: x, y, larghezza, altezza in frazioni del genitore (0,0 = in alto a sinistra).
        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(x, 1 - y - h);
            rt.anchorMax = new Vector2(x + w, 1 - y);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// true se il puntatore del mouse è sopra questo rettangolo (per non far arrivare il clic alle carte sotto).
        internal static bool Contains(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            // la telecamera sta sul canvas principale: i nostri canvas annidati (OnTop) non ce l'hanno
            var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, cam);
        }
    }

    // passando sopra il pulsante si ingrandisce un poco, e torna giù quando esci
    class HoverGrow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Vector3 rest;
        float target = 1f;

        void Awake() => rest = transform.localScale;
        void OnDisable() { target = 1f; transform.localScale = rest; }
        public void OnPointerEnter(PointerEventData e) => target = 1.07f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        void Update() => transform.localScale = Vector3.Lerp(transform.localScale, rest * target, Time.unscaledDeltaTime * 14f);
    }
}
