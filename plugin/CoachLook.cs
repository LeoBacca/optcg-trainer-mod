// L'aspetto delle schermate dell'Event Coach: i colori dei disegni approvati (docs/coach-disegni/sorgenti/style.css),
// le forme (rettangoli arrotondati, cerchi, righe dei riposi) fatte da codice, le icone (plugin/assets/icone.png) e
// i pezzi copiati da Batsu (pulsanti, campo di testo, menu a tendina), come fa il resto della mod (Look.cs).
// Si disegna in pixel del disegno: tutto sta in un riquadro di 1920×991 (la misura dei disegni) che si ingrandisce
// con la finestra. Le posizioni sono quelle degli HTML dei disegni: x, y in alto a sinistra, larghezza, altezza.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class CoachLook
    {
        internal const float W = 1920, H = 991;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        internal static readonly Color Panel = Hex("#222E48"), PanelLine = Hex("#23315a"), Card = Hex("#283656"), CardLine = Hex("#34466f"),
            Label = Hex("#9aa6c2"), Soft = Hex("#c9d1e3"), Text = Hex("#e6ebf5"), FormText = Hex("#dfe5f1"), Dim = Hex("#8592b0"),
            Gold = Hex("#F5C242"), Plan = Hex("#33426a"), RestA = Hex("#2b385a"), RestB = Hex("#3b4b76"), Ready = Hex("#4a7fe0"),
            ReadyLine = Hex("#6c9cff"), Outline = Hex("#4a5a85"), Win = Hex("#5b8def"), Loss = Hex("#e5675a"), Unknown = Hex("#4a5675"),
            Even = Hex("#7c8499"), Dark = Hex("#1b1b1b"), Past = Hex("#56627f"), Spare = Hex("#3d4d78"), Row = Hex("#2f3e64"),
            Warn = Hex("#ff9a7a"), Tip = Hex("#0f1729"), Beige = Hex("#d3bf8f"), PartGold = Hex("#a8832c");
        // il calendario a quadratini: dal blu (niente) all'oro (tante partite), un colore solo (guida dei grafici)
        internal static readonly Color[] Bins = { Hex("#33426a"), Hex("#7d6a33"), Hex("#a8832c"), Hex("#d3a233"), Hex("#f5c242") };
        internal static int Bin(int games) => games == 0 ? 0 : games <= 2 ? 1 : games <= 4 ? 2 : games <= 6 ? 3 : 4;

        // le icone, nell'ordine di icone.html
        internal const int Check = 0, Star = 1, Flame = 2, Trophy = 3, Left = 4, Right = 5, Tick = 6;

        static Sprite round, circle, ring, stripes;
        static Sprite[] icons;
        static Material plain, outlined;
        internal static TMP_FontAsset Font;

        /// Il font di Batsu (da una sua scritta) e i due materiali condivisi: bianco, e bianco con il bordo scuro.
        internal static void UseFont(TMP_Text from)
        {
            if (from == null || from.font == Font) return;
            Font = from.font;
            plain = new Material(Font.material);
            plain.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            // il grassetto finto di TMP: più spesso, così si legge come quello dei disegni
            plain.SetFloat(ShaderUtilities.ID_WeightBold, 0.9f);
            outlined = new Material(plain);
            outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
            outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
            outlined.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.04f, 0.07f, 0.14f, 0.9f));
            outlined.SetFloat(ShaderUtilities.ID_FaceDilate, 0.12f); // le scritte grandi più piene, come il grassetto dei disegni
        }

        // ---- le forme, fatte una volta ----

        static Sprite Shape(int size, Func<float, float, float> alpha, int border = 0, bool tile = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f))));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // rettangolo con gli angoli tondi (raggio 8 su 32: con pixelsPerUnitMultiplier si fa più piccolo)
        static Sprite RoundSprite => round ??= Shape(32, (x, y) =>
        {
            float dx = Mathf.Max(0, Mathf.Abs(x - 16) - 8), dy = Mathf.Max(0, Mathf.Abs(y - 16) - 8);
            return 8.5f - Mathf.Sqrt(dx * dx + dy * dy);
        }, 12);
        static Sprite CircleSprite => circle ??= Shape(128, (x, y) => 63.5f - Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64)));
        static Sprite RingSprite => ring ??= Shape(128, (x, y) =>
        {
            float d = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
            return Mathf.Min(63.5f - d, d - 55.5f);
        });
        // le righe dei giorni di riposo (135°, ogni 9 px circa): si ripetono, e si tingono con il colore chiaro
        static Sprite StripeSprite => stripes ??= Shape(12, (x, y) => ((int)(x + 12 - y) % 12) < 5 ? 1 : 0, 0, true);

        internal static Sprite Icon(int i)
        {
            if (icons == null)
            {
                icons = new Sprite[7];
                try
                {
                    using var stream = typeof(CoachLook).Assembly.GetManifestResourceStream("OPTCGTrainer.icone.png");
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    ImageConversion.LoadImage(tex, bytes);
                    tex.filterMode = FilterMode.Bilinear;
                    for (int k = 0; k < icons.Length; k++) icons[k] = Sprite.Create(tex, new Rect(k * 128, 0, 128, 128), new Vector2(0.5f, 0.5f));
                }
                catch (Exception e) { Plugin.Log.LogWarning("icone del coach non caricate: " + e.Message); }
            }
            return i >= 0 && i < icons.Length ? icons[i] : null;
        }

        // ---- i pezzi ----

        /// Un rettangolo nel genitore, in pixel del disegno (x, y in alto a sinistra).
        internal static RectTransform At(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Put(rt, x, y, w, h);
            return rt;
        }

        internal static void Put(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        internal static Image Box(Transform parent, float x, float y, float w, float h, Color color, float radius = 0, bool clicks = false)
        {
            var img = At(parent, "Box", x, y, w, h).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = clicks;
            if (radius > 0)
            {
                img.sprite = RoundSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 8f / radius;
            }
            return img;
        }

        internal static Image Circle(Transform parent, float cx, float cy, float d, Color color, bool ringOnly = false)
        {
            var img = At(parent, "Circle", cx - d / 2, cy - d / 2, d, d).gameObject.AddComponent<Image>();
            img.sprite = ringOnly ? RingSprite : CircleSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// Le righe dei giorni di riposo sopra un rettangolo (il colore chiaro delle righe; sotto va il colore di fondo).
        internal static Image Stripes(Transform parent, float x, float y, float w, float h, Color color)
        {
            var img = At(parent, "Stripes", x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = StripeSprite;
            img.type = Image.Type.Tiled;
            img.pixelsPerUnitMultiplier = 12f / 13f; // la trama di 12 px si ripete ogni 13: righe ogni 9 px circa, come i disegni
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// Un bordo dentro il rettangolo (come il box-shadow inset dei disegni): quattro righe sottili.
        internal static void Frame(Transform parent, float x, float y, float w, float h, Color color, float t)
        {
            Box(parent, x, y, w, t, color);
            Box(parent, x, y + h - t, w, t, color);
            Box(parent, x, y + t, t, h - 2 * t, color);
            Box(parent, x + w - t, y + t, t, h - 2 * t, color);
        }

        internal static Image Picture(Transform parent, int icon, float x, float y, float w, float h, Color? tint = null)
        {
            var img = At(parent, "Icon", x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = Icon(icon);
            img.preserveAspect = true;
            img.color = tint ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        /// Una scritta con il font di Batsu. y è il bordo alto della riga; out = bianca con il bordo scuro (le scritte grandi).
        // i font del sim non hanno il grassetto: TMP lo finge allargando le lettere (boldSpacing 7), qui si stringono di nuovo
        internal const float BoldTighten = -5;

        /// Una scritta con il font di Batsu. y è il bordo alto della riga; out = bianca con il bordo scuro (le scritte grandi).
        /// Con più misure (<size=…>) size è quella più piccola: la spaziatura del grassetto si conta su quella.
        internal static TextMeshProUGUI Write(Transform parent, float x, float y, float w, float h, string text, float size, Color color,
            bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool out_ = false)
        {
            var t = At(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) { t.font = Font; t.fontSharedMaterial = out_ ? outlined : plain; }
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.characterSpacing = bold ? BoldTighten : 0;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = Vector4.zero;
            return t;
        }

        /// Le etichette piccole in maiuscolo e spaziate (TODAY · MON 9 NOV, STREAK, PASS…).
        internal static TextMeshProUGUI Caps(Transform parent, float x, float y, float w, string text, float size = 15, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool upper = true)
        {
            // upper = false: il testo è già in maiuscolo (con dentro un <noparse> che non va toccato)
            var t = Write(parent, x, y, w, size * 1.4f, upper ? text.ToUpperInvariant() : text, size, Label, true, align);
            t.characterSpacing = 14 + BoldTighten;
            return t;
        }

        /// Un pulsante di Batsu (color carta, o tinto di blu) con la scritta e il clic nostri, nel nostro riquadro.
        internal static GameObject Button(Transform parent, GameObject template, float x, float y, float w, float h, string label, bool blue, UnityAction click, float size = 20)
        {
            var copy = Look.CloneButton(template, "Button", label, blue ? Look.Blue : Color.white, click);
            copy.transform.SetParent(parent, false);
            var rt = (RectTransform)copy.transform;
            Put(rt, x, y, w, h);
            // i bordi della cornice di Batsu più sottili sui pulsanti piccoli (come .btn.sm dei disegni)
            foreach (var img in copy.GetComponentsInChildren<Image>(true))
                if (img.type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = h < 70 ? 2.2f : 1.3f;
            var text = copy.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.enableAutoSizing = false;
                text.fontSize = size;
                text.alignment = TextAlignmentOptions.Center;
                text.margin = Vector4.zero;
                text.characterSpacing = blue ? BoldTighten + 2 : 0;
                // un materiale solo per le scritte di tutti i nostri pulsanti: Look.WhiteLabel ne crea uno per pulsante, e a ogni
                // rifacimento della pagina se ne accumulerebbero (Unity non li libera da solo)
                var own = text.fontSharedMaterial;
                text.fontSharedMaterial = ButtonMaterial(text.font, own);
                if (own != null && own != text.fontSharedMaterial && own.name.EndsWith("(Instance)")) UnityEngine.Object.Destroy(own);
                // sul color carta la scritta resta scura, come quelle di Batsu (la faccia del materiale è bianca: decide il colore)
                if (!blue) { text.fontStyle &= ~FontStyles.Bold; text.color = new Color(0.07f, 0.07f, 0.08f); }
                var trt = text.rectTransform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            }
            return copy;
        }

        static readonly System.Collections.Generic.Dictionary<TMP_FontAsset, Material> buttonMaterials = new System.Collections.Generic.Dictionary<TMP_FontAsset, Material>();

        static Material ButtonMaterial(TMP_FontAsset font, Material from)
        {
            if (font == null) return from;
            if (!buttonMaterials.TryGetValue(font, out var m) || m == null)
            {
                m = new Material(from ?? font.material) { name = "OPTCGTrainer coach button" };
                m.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
                buttonMaterials[font] = m;
            }
            return m;
        }

        /// Un'area che si clicca (senza disegno: il disegno sta sotto), e che sa quando il mouse entra ed esce.
        internal static Hover Hot(Transform parent, float x, float y, float w, float h, Action click, Action enter = null, Action exit = null)
        {
            var img = At(parent, "Hot", x, y, w, h).gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            var hover = img.gameObject.AddComponent<Hover>();
            hover.click = click; hover.enter = enter; hover.exit = exit;
            return hover;
        }
    }

    /// Clic, entrata e uscita del mouse su un'area del coach.
    class Hover : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action click, enter, exit;
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Safe(click); }
        public void OnPointerEnter(PointerEventData e) => Safe(enter);
        public void OnPointerExit(PointerEventData e) => Safe(exit);
        static void Safe(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception ex) { Plugin.Log.LogWarning("coach: " + ex.Message); } // un errore della mod non deve mai fermare il sim
        }
    }
}
