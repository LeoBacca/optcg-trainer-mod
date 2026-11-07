// La foto da condividere (Discord, coach): l'icona con la macchina fotografica in basso a destra salva un'immagine con
// il tavolo e, a destra, quello che serve per capire la situazione e che dallo screenshot non si vede:
//   il tuo trash · il trash dell'avversario · le carte che hai messo in fondo al tuo mazzo · le carte note in mano all'avversario
// e in basso a destra il logo di OPTCG Trainer. Si salva in Immagini\OPTCG Trainer.
//
// Si mostra solo quello che il giocatore sa davvero (le regole sono quelle del sito, src/core/engine/moves.js e deck-bottom.js):
//   fondo del mazzo  le carte messe sotto una per una (da un'altra zona, o spostate dalla cima al fondo); una rimescolata lo azzera.
//                    L'ordine vero del mazzo il sim lo conosce, ma mostrarlo sarebbe barare.
//   mano avversaria  nota se ci arriva dal campo, dallo stage o dal trash, o con una rivelazione ("Reveal and Draw", "revealed:");
//                    non nota se pescata o presa dalla Life. Smette di essere nota quando esce dalla mano.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class Knowledge
    {
        enum Z { None, Deck, Hand, Trash, Field, Stage, Leader, Life }

        static readonly Dictionary<GameObject, Z> zone = new Dictionary<GameObject, Z>();
        static readonly Dictionary<GameObject, int> deckIndex = new Dictionary<GameObject, int>();
        static readonly HashSet<GameObject> bottom = new HashSet<GameObject>();   // le mie carte messe in fondo al mazzo
        static readonly HashSet<GameObject> oppKnown = new HashSet<GameObject>(); // carte note nella mano dell'avversario
        static int logSeen, generation = -1;
        internal static bool Shuffled; // il sim ha rimescolato il mio mazzo (agganci a ShuffleDeck & co.)

        internal static string Id(GameObject card)
        {
            var c = card != null ? card.GetComponent<CardLogicScript>() : null;
            return c != null && c.myCard.cardDef != null ? c.myCard.cardDef.cardID : null;
        }

        /// A ogni fotogramma in partita (GameUi.Tick): confronta le zone di adesso con quelle del fotogramma prima.
        internal static void Track(GameplayLogicScript game)
        {
            if (generation != Match.Generation) { generation = Match.Generation; zone.Clear(); deckIndex.Clear(); bottom.Clear(); oppKnown.Clear(); logSeen = 0; }
            var players = game.Lps_Players;
            if (players == null || players.Count < 2) return;
            PlayerState me = players[0], opp = players[1];

            // le righe nuove del log del sim (per le rivelazioni)
            var log = game.currentCombatLog;
            string fresh = "";
            if (log != null)
            {
                if (logSeen > log.Count) logSeen = 0;
                if (log.Count > logSeen) fresh = string.Join("\n", log.Skip(logSeen));
                logSeen = log.Count;
            }

            // il fondo del mio mazzo (indice 0 = la carta più in fondo; la cima è l'ultima, da lì si pesca)
            var deck = me.Lgo_MyDeck;
            bottom.RemoveWhere(c => c == null || !deck.Contains(c));
            if (Shuffled) { bottom.Clear(); Shuffled = false; }
            else
            {
                for (int i = 0; i < deck.Count; i++)
                {
                    var c = deck[i];
                    zone.TryGetValue(c, out var was);
                    bool placed = (was != Z.None && was != Z.Deck) || (was == Z.Deck && deckIndex.TryGetValue(c, out var j) && j - i > 1);
                    if (placed && i <= KnownBottom(deck).Count) bottom.Add(c);
                }
            }

            // la mano dell'avversario
            foreach (var c in opp.Lgo_MyHand)
            {
                if (c == null || oppKnown.Contains(c)) continue;
                zone.TryGetValue(c, out var was);
                var id = Id(c);
                bool told = id != null && fresh.Contains(id);
                if (was == Z.Trash || was == Z.Field || was == Z.Stage || was == Z.Leader) oppKnown.Add(c);
                else if (was == Z.Deck && told && fresh.Contains("Reveal")) oppKnown.Add(c);
                else if (was == Z.Hand && told && fresh.Contains(" revealed")) oppKnown.Add(c);
            }
            oppKnown.RemoveWhere(c => c == null || !opp.Lgo_MyHand.Contains(c));

            zone.Clear();
            deckIndex.Clear();
            Fill(me);
            Fill(opp);
            for (int i = 0; i < deck.Count; i++) if (deck[i] != null) deckIndex[deck[i]] = i;
        }

        static void Fill(PlayerState p)
        {
            void Put(List<GameObject> list, Z z) { if (list != null) foreach (var c in list) if (c != null) zone[c] = z; }
            Put(p.Lgo_MyDeck, Z.Deck);
            Put(p.Lgo_MyHand, Z.Hand);
            Put(p.Lgo_MyTrash, Z.Trash);
            Put(p.Lgo_MyDeploy, Z.Field);
            Put(p.Lgo_MyStage, Z.Stage);
            Put(p.Lgo_MyLeader, Z.Leader);
            Put(p.Lgo_MyLifeDeck, Z.Life);
        }

        /// Le carte in fondo al mio mazzo che conosco, dalla più in fondo: le prime del mazzo finché sono tra quelle messe sotto.
        internal static List<GameObject> KnownBottom(List<GameObject> deck)
        {
            var list = new List<GameObject>();
            foreach (var c in deck) { if (!bottom.Contains(c)) break; list.Add(c); }
            return list;
        }

        internal static List<GameObject> OppKnownHand(PlayerState opp) => opp.Lgo_MyHand.Where(c => oppKnown.Contains(c)).ToList();
    }

    // le rimescolate del mazzo: dopo, il fondo non si conosce più
    [HarmonyPatch(typeof(GameplayLogicScript), "ShuffleDeck")]
    static class OnShuffleDeck { static void Postfix() => Knowledge.Shuffled = true; }

    [HarmonyPatch(typeof(GameplayLogicScript), "ShuffleWithString")]
    static class OnShuffleWithString { static void Postfix() => Knowledge.Shuffled = true; }

    [HarmonyPatch(typeof(GameplayLogicScript), "ShuffleHandIntoDeck")]
    static class OnShuffleHandIntoDeck { static void Postfix() => Knowledge.Shuffled = true; }

    static class ShareShot
    {
        internal static GameObject Button;
        static TextMeshProUGUI toast;
        static float toastUntil;
        static bool busy;
        internal static bool Hiding; // durante lo scatto i pulsanti della mod non si vedono
        const int Layer = 31;        // l'immagine si compone fuori schermo, su uno strato che le telecamere del sim non guardano
        static readonly Color Navy = new Color(0.04f, 0.07f, 0.16f);

        // ---- l'icona ----

        internal static void Build(GameplayLogicScript game, GameObject template)
        {
            Button = Look.CloneButton(template, "OPTCGTrainerShot", "", Look.Blue, () => Shoot(game));
            var le = Button.GetComponent<LayoutElement>() ?? Button.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = (RectTransform)Button.transform;
            // agganciata all'angolo in basso a destra del canvas principale (cioè dello schermo), sotto End Turn: resta lì anche
            // quando la finestra cambia misura (Focus mode a schermo intero). Messa a posto una volta sola, usciva dallo schermo.
            var root = (RectTransform)rt.GetComponentInParent<Canvas>().rootCanvas.transform;
            var mine = new Vector3[4];
            rt.GetWorldCorners(mine);
            float size = (mine[1].y - mine[0].y) / Mathf.Max(0.0001f, root.lossyScale.y); // la stessa misura, nelle unità del canvas
            rt.SetParent(root, false);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-size * 0.18f, size * 0.18f);
            rt.SetAsLastSibling();

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)icon.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = new Vector2(0.16f, 0.16f); irt.anchorMax = new Vector2(0.84f, 0.84f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            var img = icon.GetComponent<Image>();
            img.sprite = CameraGlyph();
            img.preserveAspect = true;
            img.raycastTarget = false;

            var font = template.GetComponentInChildren<TMP_Text>(true);
            toast = Look.NewText(rt, font, "Saved", size * 0.28f, TextAlignmentOptions.BottomRight);
            var trt = toast.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(1, 0);
            trt.anchoredPosition = new Vector2(0, size * 0.15f);
            trt.sizeDelta = new Vector2(size * 7, size * 0.8f);
            toast.textWrappingMode = TextWrappingModes.NoWrap;
            toast.outlineWidth = 0.25f;
            toast.outlineColor = new Color32(10, 20, 45, 255);
            toast.gameObject.SetActive(false);
        }

        internal static void Tick()
        {
            if (toast != null && toast.gameObject.activeSelf && Time.realtimeSinceStartup > toastUntil) toast.gameObject.SetActive(false);
        }

        static void Toast(string text)
        {
            toast.text = text;
            toast.gameObject.SetActive(true);
            toastUntil = Time.realtimeSinceStartup + 2.5f;
        }

        // una macchina fotografica bianca su fondo trasparente, disegnata pixel per pixel (niente immagini da installare)
        static Sprite glyph;
        static Sprite CameraGlyph()
        {
            if (glyph != null) return glyph;
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float Box(float x, float y, float cx, float cy, float hw, float hh, float r)
            {
                float dx = Mathf.Max(Mathf.Abs(x - cx) - (hw - r), 0), dy = Mathf.Max(Mathf.Abs(y - cy) - (hh - r), 0);
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float body = Mathf.Min(Box(fx, fy, 48, 44, 40, 26, 9), Box(fx, fy, 40, 72, 13, 6, 3)); // corpo e mirino
                    float lens = Mathf.Sqrt((fx - 48) * (fx - 48) + (fy - 44) * (fy - 44));
                    float shape = Mathf.Max(body, -(lens - 17));       // il corpo con il buco dell'obiettivo
                    shape = Mathf.Min(shape, lens - 10);                // la lente dentro
                    float a = Mathf.Clamp01(0.5f - shape);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            glyph = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return glyph;
        }

        // ---- lo scatto ----

        static void Shoot(GameplayLogicScript game)
        {
            if (!busy) Plugin.Instance.StartCoroutine(Capture(game));
        }

        static IEnumerator Capture(GameplayLogicScript game)
        {
            busy = true;
            Hiding = true;
            Texture2D board = null;
            try
            {
                yield return null;                     // un fotogramma senza i pulsanti della mod
                yield return new WaitForEndOfFrame();
                board = ScreenCapture.CaptureScreenshotAsTexture();
                Hiding = false;
                var path = Save(Compose(game, board));
                Toast("Saved in Pictures › OPTCG Trainer");
                Plugin.Log.LogInfo("foto salvata: " + path);
            }
            finally
            {
                Hiding = false;
                busy = false;
                if (board != null) UnityEngine.Object.Destroy(board);
            }
        }

        static string Save(byte[] png)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OPTCG Trainer");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"OPTCG {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            File.WriteAllBytes(path, png);
            return path;
        }

        /// L'immagine: il tavolo a sinistra (solo tavolo e mani: la parte destra del sim, con End Turn, Back to Main e l'audio,
        /// non serve), la colonna con i quattro gruppi di carte a destra, il logo in basso a destra. Le copie della stessa carta
        /// sono un'immagine sola con il numero sopra, così anche un trash da 30 carte resta leggibile.
        /// Si compone su un canvas fuori schermo e lo si fotografa con una telecamera tutta nostra.
        static byte[] Compose(GameplayLogicScript game, Texture2D board)
        {
            var players = game.Lps_Players;
            PlayerState me = players[0], opp = players[1];
            var groups = new List<(string title, List<string> ids)>
            {
                ("YOUR TRASH", me.Lgo_MyTrash.Select(Knowledge.Id).Where(x => x != null).ToList()),
                ("OPPONENT'S TRASH", opp.Lgo_MyTrash.Select(Knowledge.Id).Where(x => x != null).ToList()),
                ("YOUR DECK BOTTOM", Knowledge.KnownBottom(me.Lgo_MyDeck).Select(Knowledge.Id).Where(x => x != null).ToList()),
                ("KNOWN IN OPPONENT'S HAND", Knowledge.OppKnownHand(opp).Select(Knowledge.Id).Where(x => x != null).ToList()),
            };
            // una carta sola per ogni carta diversa, con quante copie (nell'ordine in cui sono arrivate)
            var stacks = groups.Select(g => g.ids.GroupBy(x => x).Select(x => (id: x.Key, n: x.Count())).ToList()).ToList();
            int turn = game.gsv_CurrentGame != null ? game.gsv_CurrentGame.iTurnNumber : 0;

            var crop = BoardArea(game, board.width, board.height);
            int H = board.height, BW = Mathf.RoundToInt(crop.width), CW = Mathf.RoundToInt(H * 0.74f), W = BW + CW;
            var font = game.go_DownloadLog.GetComponentInChildren<TMP_Text>(true);
            var root = new GameObject("OPTCGTrainerShotCanvas", typeof(RectTransform));
            GameObject camGo = null;
            RenderTexture target = null;
            Texture2D shot = null;
            try
            {
                root.transform.position = new Vector3(0, -50000, 0);
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var rrt = (RectTransform)root.transform;
                rrt.sizeDelta = new Vector2(W, H);
                rrt.localScale = Vector3.one;

                var boardImg = Child<RawImage>(rrt, "Board", 0, 0, BW, H);
                boardImg.texture = board;
                boardImg.uvRect = new Rect(crop.x / board.width, 0, crop.width / board.width, 1); // solo la parte del tavolo
                var column = Child<Image>(rrt, "Column", BW, 0, CW, H);
                column.color = Navy;
                var line = Child<Image>(rrt, "Edge", BW, 0, Mathf.Max(2, H / 360f), H);
                line.color = new Color(0.96f, 0.76f, 0.25f, 0.9f);

                float pad = CW * 0.055f, x0 = BW + pad, innerW = CW - 2 * pad;
                float titleH = H * 0.07f, headerH = H * 0.036f, logoH = H * 0.1f, gap = Mathf.Max(3, H / 200f), groupGap = H * 0.022f;
                Text(rrt, font, turn > 0 ? $"TURN {turn}" : "MULLIGAN", x0, pad * 0.6f, innerW, titleH, titleH * 0.62f, TextAlignmentOptions.Left, Color.white);

                // la misura delle carte più grande che fa stare tutto
                float top = pad * 0.6f + titleH + groupGap, room = H - top - logoH - pad, tw = innerW * 0.17f;
                for (; tw > innerW * 0.05f; tw -= 2)
                {
                    float th = tw * 1.396f, perRow = Mathf.Max(1, Mathf.Floor((innerW + gap) / (tw + gap)));
                    float need = stacks.Sum(g => headerH + (g.Count == 0 ? headerH : Mathf.Ceil(g.Count / perRow) * (th + gap))) + groupGap * (groups.Count - 1);
                    if (need <= room) break;
                }
                float cardH = tw * 1.396f;
                int cols = Mathf.Max(1, (int)Mathf.Floor((innerW + gap) / (tw + gap)));
                float y = top;
                for (int g = 0; g < groups.Count; g++)
                {
                    var (title, ids) = groups[g];
                    var cards = stacks[g];
                    Text(rrt, font, $"{title}  <color={Look.GoldHex}>{ids.Count}</color>", x0, y, innerW, headerH, headerH * 0.78f, TextAlignmentOptions.Left, Color.white);
                    y += headerH;
                    if (cards.Count == 0)
                    {
                        Text(rrt, font, "—", x0, y, innerW, headerH, headerH * 0.7f, TextAlignmentOptions.Left, new Color(1, 1, 1, 0.45f));
                        y += headerH;
                    }
                    for (int i = 0; i < cards.Count; i++)
                    {
                        float cx = x0 + (i % cols) * (tw + gap), cy = y + (i / cols) * (cardH + gap);
                        var (id, n) = cards[i];
                        var card = Child<Image>(rrt, id, cx, cy, tw, cardH);
                        var sprite = CardSprite(game, id);
                        if (sprite != null) card.sprite = sprite;
                        else
                        {
                            card.color = new Color(0.2f, 0.24f, 0.35f);
                            Text(rrt, font, id, cx, cy, tw, cardH, tw * 0.16f, TextAlignmentOptions.Center, Color.white);
                        }
                        if (n > 1)
                        {
                            // il numero delle copie: un cerchio oro con il numero scuro, grande, in alto a destra della carta
                            float d = tw * 0.5f;
                            var badge = Child<Image>(rrt, "Count", cx + tw - d * 0.92f, cy + d * 0.08f, d, d);
                            badge.sprite = Circle();
                            badge.color = new Color(0.96f, 0.76f, 0.25f);
                            Text(rrt, font, n.ToString(), cx + tw - d * 0.92f, cy + d * 0.08f, d, d, d * 0.72f, TextAlignmentOptions.Center, Navy);
                        }
                    }
                    if (cards.Count > 0) y += Mathf.Ceil(cards.Count / (float)cols) * (cardH + gap);
                    y += groupGap;
                }

                // il logo in basso a destra, un po' trasparente
                float ls = logoH * 0.95f;
                var logo = Child<Image>(rrt, "Logo", W - pad - ls, H - pad * 0.5f - ls, ls, ls);
                logo.sprite = Plugin.LogoSprite;
                logo.preserveAspect = true;
                logo.color = new Color(1, 1, 1, 0.9f);
                Text(rrt, font, $"OPTCG <color={Look.GoldHex}>TRAINER</color>", W - pad - ls - innerW, H - pad * 0.5f - ls, innerW - ls * 0.15f, ls, ls * 0.34f,
                    TextAlignmentOptions.Right, new Color(1, 1, 1, 0.9f));

                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                Canvas.ForceUpdateCanvases();
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();

                camGo = new GameObject("OPTCGTrainerShotCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = H / 2f;
                cam.transform.position = root.transform.position + new Vector3(0, 0, -100);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Navy;
                target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = target;
                canvas.worldCamera = cam;
                cam.Render();

                var was = RenderTexture.active;
                RenderTexture.active = target;
                shot = new Texture2D(W, H, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                shot.Apply();
                RenderTexture.active = was;
                return shot.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
            }
        }

        /// La parte dello schermo con il tavolo e le mani (in pixel, dal basso a sinistra come lo screenshot): dai tappetini e da
        /// tutte le carte in gioco, con un po' di margine. Funziona in finestra e a schermo intero, qualunque sia la disposizione.
        static Rect BoardArea(GameplayLogicScript game, int w, int h)
        {
            float min = float.MaxValue, max = float.MinValue;
            void Add(GameObject go)
            {
                if (go == null || !go.activeInHierarchy || !TryScreenRect(go, out var r)) return;
                min = Mathf.Min(min, r.xMin);
                max = Mathf.Max(max, r.xMax);
            }
            Add(game.go_P0Board);
            Add(game.go_P1Board);
            foreach (var p in game.Lps_Players)
                foreach (var list in new[] { p.Lgo_MyHand, p.Lgo_MyDeck, p.Lgo_MyLifeDeck, p.Lgo_MyTrash, p.Lgo_MyLeader, p.Lgo_MyStage, p.Lgo_MyDeploy, p.Lgo_MyDonCostArea })
                    if (list != null) foreach (var c in list) Add(c);
            if (min >= max) return new Rect(0, 0, w, h);
            float margin = w * 0.012f;
            float x0 = Mathf.Clamp(min - margin, 0, w), x1 = Mathf.Clamp(max + margin, 0, w);
            return x1 - x0 < w * 0.3f ? new Rect(0, 0, w, h) : new Rect(x0, 0, x1 - x0, h); // se qualcosa non torna, lo schermo intero
        }

        static bool TryScreenRect(GameObject go, out Rect r)
        {
            r = default;
            var corners = new Vector3[4];
            if (go.transform is RectTransform rt)
            {
                var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                rt.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++) corners[i] = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            }
            else
            {
                var rend = go.GetComponentInChildren<Renderer>();
                if (rend == null || Camera.main == null) return false;
                var b = rend.bounds;
                corners[0] = Camera.main.WorldToScreenPoint(b.min);
                corners[1] = Camera.main.WorldToScreenPoint(b.max);
                corners[2] = corners[0];
                corners[3] = corners[1];
            }
            float x0 = corners.Min(c => c.x), x1 = corners.Max(c => c.x), y0 = corners.Min(c => c.y), y1 = corners.Max(c => c.y);
            r = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        static Sprite circle;
        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(n / 2f - 1 - d + 0.5f));
                }
            tex.SetPixels(px);
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return circle;
        }

        // l'immagine piccola della carta, presa dal sim (che usa anche le immagini personalizzate di chi gioca)
        static Sprite CardSprite(GameplayLogicScript game, string id)
        {
            try { return game.card_Database != null ? game.card_Database.GetCardImage(id, SpriteState.Thumbnail) : null; }
            catch (Exception) { return null; }
        }

        // un rettangolo in pixel dall'angolo in alto a sinistra dell'immagine
        static T Child<T>(RectTransform parent, string name, float x, float y, float w, float h) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var c = go.AddComponent<T>();
            if (c is Graphic g) g.raycastTarget = false;
            return c;
        }

        static void Text(RectTransform parent, TMP_Text font, string text, float x, float y, float w, float h, float size, TextAlignmentOptions align, Color color)
        {
            var t = Child<TextMeshProUGUI>(parent, "Text", x, y, w, h);
            if (font != null) t.font = font.font;
            t.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = align;
            t.color = color;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // il font di Batsu ha righe alte: con "Ellipsis" una riga che non sta tutta in altezza sparirebbe del tutto
            t.overflowMode = TextOverflowModes.Overflow;
            t.verticalAlignment = VerticalAlignmentOptions.Middle;
        }
    }
}
