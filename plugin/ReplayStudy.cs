using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    internal static class ReplayStudy
    {
        sealed class Entry { public string text; public List<InkStroke> strokes; }
        static GameplayLogicScript game;
        static GameObject root, notes, marker, bottom, shareBanner;
        static TMP_InputField input;
        static TMP_Text noteTitle, noteStatus, penTitle, bottomFont, shareBannerText;
        static Material bottomTextMat;
        static RectTransform bottomViewport, bottomContent;
        static ScrollRect bottomScroll;
        static Camera bottomCam;
        static ReplayInk ink;
        static SortedDictionary<int, Entry> entries;
        static string file, bottomKey, bottomHovering;
        static bool canSave, wasNotes, wasMarker, wasBottom, wasBanner;
        static int moment, bottomPlayer, shareRequestToken;
        static float bottomStep, bottomTableEdge, bottomNextTable, shareBannerUntil;
        static int bottomTableW, bottomTableH;
        static readonly List<(RectTransform rt, string id)> bottomCells = new List<(RectTransform, string)>();
        static readonly Vector3[] bottomCorners = new Vector3[4];

        static readonly Color Navy = new Color32(0x16, 0x22, 0x4a, 0xff), Edge = new Color32(0x0d, 0x16, 0x30, 0xff);
        static readonly Color Gold = new Color32(0xf2, 0xc2, 0x30, 0xff), Soft = new Color32(0x9f, 0xb0, 0xdc, 0xff);
        static readonly Color PillOff = new Color32(0x24, 0x35, 0x6b, 0xff);
        const float CardRatio = 1.396f;

        internal static int NoteVersion { get; private set; }
        internal static bool Typing => input != null && notes != null && notes.activeSelf && input.isFocused;
        internal static bool DrawOpen => ink != null && ink.editing;
        internal static bool NotesOpen => notes != null && notes.activeSelf;
        internal static bool BottomOpen => bottom != null && bottom.activeSelf;

        static GameObject Area(Transform parent, string name, float x, float y, float w, float h, Color? color = null)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            Look.Place((RectTransform)obj.transform, x, y, w, h);
            if (color.HasValue) obj.AddComponent<Image>().color = color.Value;
            return obj;
        }
        static TMP_Text Label(GameObject parent, string text, float x, float y, float w, float h)
        {
            var font = game.go_DownloadLog.GetComponentInChildren<TMP_Text>(true);
            var t = Look.NewText(parent.transform, font, "Label", 22, TextAlignmentOptions.Left);
            Look.Place(t.rectTransform, x,y,w,h);
            t.text = text; t.richText = false; t.enableAutoSizing = true; t.fontSizeMin = 8; t.fontSizeMax = 26;
            return t;
        }
        static GameObject Button(GameObject parent, string label, Action click, float x, float y, float w, float h)
        {
            var b = Look.CloneButton(game.go_DownloadLog, "Study " + label, label, Look.Blue, () =>
            {
                click();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });
            b.transform.SetParent(parent.transform, false);
            Look.Place((RectTransform)b.transform, x,y,w,h);
            var t = b.GetComponentInChildren<TMP_Text>(true);
            t.margin = Vector4.zero; t.enableAutoSizing = true; t.fontSizeMin = 8; t.fontSizeMax = 22;
            return b;
        }
        internal static void Build(GameplayLogicScript g, string path)
        {
            game = g; bottomPlayer = 1; bottomKey = null; bottomHovering = null; moment = int.MinValue;
            bottomTableW = bottomTableH = 0; bottomNextTable = 0; bottomStep = 0; shareBannerUntil = 0;
            wasNotes = wasMarker = wasBottom = wasBanner = false;
            bottomCells.Clear();
            game.go_LogView.SetActive(false);
            entries = new SortedDictionary<int, Entry>();
            canSave = false;
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                string key = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                file = Path.Combine(Application.persistentDataPath, "ReplayNotes", key + ".json");
                if (File.Exists(file))
                    entries = JsonConvert.DeserializeObject<SortedDictionary<int, Entry>>(File.ReadAllText(file)) ?? entries;
                canSave = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Replay notes unavailable: " + e.Message); }
            NoteVersion++;
            var rootCanvas = game.go_ChoiceButton1.GetComponentInParent<Canvas>(true).rootCanvas;
            bottomCam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
            bottomFont = game.go_DownloadLog.GetComponentInChildren<TMP_Text>(true);
            var canvas = rootCanvas.transform;
            root = Area(canvas, "Replay study", 0,0,1,1);
            GameUi.OnTop(root);
            root.GetComponent<Canvas>().sortingOrder = 29999;
            var sheet = Area(root.transform,"Drawing area",0,0,1,1,Color.clear);
            ink = sheet.AddComponent<ReplayInk>();
            sheet.GetComponent<Image>().raycastTarget = false;

            shareBanner = Area(root.transform, "Share banner", 0.26f, 0.015f, 0.48f, 0.052f, CoachLook.Panel);
            var bannerOutline = shareBanner.AddComponent<Outline>();
            bannerOutline.effectColor = Gold;
            bannerOutline.effectDistance = new Vector2(2, -2);
            var bannerBtn = shareBanner.AddComponent<Button>();
            bannerBtn.targetGraphic = shareBanner.GetComponent<Image>();
            bannerBtn.transition = Selectable.Transition.None;
            bannerBtn.onClick.AddListener(() =>
            {
                if (shareBanner != null) shareBanner.SetActive(false);
                shareBannerUntil = 0;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });
            shareBannerText = Label(shareBanner, "", 0.03f, 0.08f, 0.94f, 0.84f);
            shareBannerText.alignment = TextAlignmentOptions.Center;
            shareBannerText.color = Gold;
            shareBannerText.fontStyle = FontStyles.Bold;
            shareBanner.SetActive(false);

            marker = Area(root.transform,"Marker",0.025f,0.23f,0.255f,0.29f,CoachLook.Panel);
            penTitle = Label(marker,"Marker",0.04f,0.03f,0.72f,0.17f);
            Button(marker,"Close",ToggleDraw,0.74f,0.035f,0.22f,0.15f);
            string[] colors = { "Red", "Blue", "Gold", "Green", "White", "Black" };
            for (int i=0; i<colors.Length; i++)
            {
                int index = i;
                var swatch = Button(marker,"",() => { ink.color = index; ink.erasing = false; PenLabel(); },
                    0.04f+i*0.155f,0.23f,0.14f,0.17f);
                swatch.name = colors[i]; Look.SetTint(swatch, ReplayStrokeGraphic.Colors[i]);
            }
            Button(marker,"Size",() => { ink.width = (ink.width+1)%3; PenLabel(); },0.04f,0.47f,0.28f,0.19f);
            Button(marker,"Eraser",() => { ink.erasing = !ink.erasing; PenLabel(); },0.36f,0.47f,0.28f,0.19f);
            Button(marker,"Clear",ink.Clear,0.68f,0.47f,0.28f,0.19f);
            Button(marker,"Save drawing",() => { if (Save(true)) penTitle.text = "Drawing saved"; },0.04f,0.73f,0.92f,0.20f);
            marker.SetActive(false);

            notes = Area(root.transform,"Move notes",0.025f,0.23f,0.255f,0.45f,CoachLook.Panel);
            noteTitle = Label(notes,"Move notes",0.045f,0.035f,0.69f,0.095f);
            Button(notes,"Close",ToggleNotes,0.75f,0.035f,0.21f,0.10f);
            var field = Look.SafeCopy(game.go_ChatBox, notes.transform);
            Look.Place((RectTransform)field.transform,0.04f,0.18f,0.92f,0.40f);
            input = field.GetComponent<TMP_InputField>();
            input.onEndEdit = new TMP_InputField.SubmitEvent(); input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onSelect = new TMP_InputField.SelectionEvent(); input.onDeselect = new TMP_InputField.SelectionEvent();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.lineType = TMP_InputField.LineType.MultiLineNewline; input.characterLimit = 4000;
            input.textComponent.fontSize = 22; input.textComponent.textWrappingMode = TextWrappingModes.Normal;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            if (input.placeholder is TMP_Text hint) { hint.text = "Write a note for this move…"; hint.fontSize = 21; }
            Button(notes,"Save note",() => Save(false),0.04f,0.63f,0.92f,0.12f);
            Button(notes,"Previous note",() => GoNote(-1),0.04f,0.79f,0.44f,0.12f);
            Button(notes,"Next note",() => GoNote(1),0.52f,0.79f,0.44f,0.12f);
            noteStatus = Label(notes,"",0.04f,0.92f,0.92f,0.07f);
            notes.SetActive(false);

            BuildBottom();
            Refresh();
        }

        static void BuildBottom()
        {
            bottom = new GameObject("Deck bottom", typeof(RectTransform), typeof(Image), typeof(Outline));
            var box = (RectTransform)bottom.transform;
            box.SetParent(root.transform, false);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = Vector2.zero;
            var bg = bottom.GetComponent<Image>();
            bg.color = Navy;
            bg.raycastTarget = true;
            var edge = bottom.GetComponent<Outline>();
            edge.effectColor = Edge;
            edge.effectDistance = new Vector2(2, -2);

            bottomViewport = NewRect(box, "List", typeof(RectMask2D), typeof(ScrollRect));
            bottomContent = NewRect(bottomViewport, "Content");
            bottomContent.anchorMin = new Vector2(0, 1);
            bottomContent.anchorMax = new Vector2(1, 1);
            bottomContent.pivot = new Vector2(0.5f, 1);
            bottomScroll = bottomViewport.GetComponent<ScrollRect>();
            bottomScroll.content = bottomContent;
            bottomScroll.viewport = bottomViewport;
            bottomScroll.horizontal = false;
            bottomScroll.movementType = ScrollRect.MovementType.Clamped;
            bottomScroll.inertia = false;

            var bar = NewRect(box, "Bar", typeof(Image), typeof(Scrollbar));
            var barImg = bar.GetComponent<Image>();
            barImg.color = new Color(1, 1, 1, 0.08f);
            barImg.raycastTarget = true;
            var area = NewRect(bar, "Area");
            Fill(area);
            var handle = NewRect(area, "Handle", typeof(Image));
            Fill(handle);
            var handleImg = handle.GetComponent<Image>();
            handleImg.color = new Color(Gold.r, Gold.g, Gold.b, 0.75f);
            handleImg.raycastTarget = true;
            var scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.transition = Selectable.Transition.None;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            bottomScroll.verticalScrollbar = scrollbar;
            bottomScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            bottom.SetActive(false);
        }

        static void PenLabel() => penTitle.text = ink.erasing ? "Marker · Eraser" : "Marker · " + new[] { "Fine","Medium","Bold" }[ink.width];
        static void Editing(bool on)
        {
            if (ink == null) return;
            ink.editing = on;
            ink.GetComponent<Image>().raycastTarget = on;
            if (marker != null) marker.SetActive(on);
        }
        internal static void ToggleDraw()
        {
            bool on = !ink.editing;
            if (on) { ReplayRoom.Pause(); HideNotes(); PenLabel(); }
            Editing(on);
        }
        internal static void EndDrawing() { if (ink != null) { Editing(false); ink.Clear(); } }
        internal static void ToggleNotes()
        {
            if (notes.activeSelf) { HideNotes(); return; }
            ReplayRoom.Pause(); Editing(false);
            notes.SetActive(true); LoadText();
        }
        static void HideNotes()
        {
            if (notes == null || !notes.activeSelf) return;
            if (!SaveDraft()) return;
            input.DeactivateInputField(); notes.SetActive(false);
        }
        internal static List<int> NoteMoments() => entries == null
            ? new List<int>()
            : entries.Where(kv => !string.IsNullOrWhiteSpace(kv.Value?.text) || (kv.Value?.strokes != null && kv.Value.strokes.Count > 0))
                .Select(kv => kv.Key).ToList();
        internal static string NotePreview(int beat)
        {
            if (entries == null || !entries.TryGetValue(beat, out var e) || e == null) return null;
            if (!string.IsNullOrWhiteSpace(e.text))
            {
                string t = e.text.Replace("\r", " ").Replace("\n", " ").Trim();
                return t.Length > 60 ? t.Substring(0, 57) + "..." : t;
            }
            return e.strokes != null && e.strokes.Count > 0 ? "[Drawing]" : null;
        }
        internal static void JumpToNote(int beat)
        {
            if (notes != null && notes.activeSelf && !SaveDraft()) return;
            ReplayRoom.Seek(beat);
            Refresh();
            if (entries != null && entries.TryGetValue(beat, out var e) && !string.IsNullOrWhiteSpace(e?.text) && notes != null)
            {
                ReplayRoom.Pause();
                Editing(false);
                notes.SetActive(true);
                LoadText();
            }
        }
        internal static void SetHiddenForShot(bool hide)
        {
            if (hide)
            {
                wasNotes = notes != null && notes.activeSelf;
                wasMarker = marker != null && marker.activeSelf;
                wasBottom = bottom != null && bottom.activeSelf;
                wasBanner = shareBanner != null && shareBanner.activeSelf;
                if (notes != null) notes.SetActive(false);
                if (marker != null) marker.SetActive(false);
                if (bottom != null) bottom.SetActive(false);
                if (shareBanner != null) shareBanner.SetActive(false);
                BottomHover(null);
            }
            else
            {
                if (notes != null && wasNotes) notes.SetActive(true);
                if (marker != null && wasMarker) marker.SetActive(true);
                if (bottom != null && wasBottom) bottom.SetActive(true);
                if (shareBanner != null && wasBanner) shareBanner.SetActive(true);
                wasNotes = wasMarker = wasBottom = wasBanner = false;
            }
        }
        internal static void ShowBanner(string message, float seconds = 4f)
        {
            if (shareBanner == null || shareBannerText == null) return;
            shareBannerText.text = message ?? "";
            shareBannerUntil = Time.realtimeSinceStartup + seconds;
            shareBanner.SetActive(true);
        }
        internal static void ShareCurrentReplay()
        {
            if (ReplayRoom.CurrentLogPath == null) return;
            int token = ++shareRequestToken;
            ShowBanner("Creating public replay link...", 15f);
            Account.ShareReplay(ReplayRoom.CurrentLogPath, (ok, url, msg) =>
            {
                if (token != shareRequestToken || shareBanner == null) return;
                ShowBanner(ok ? "Link copied! " + url : (string.IsNullOrEmpty(msg) ? "Could not share replay" : msg), 7f);
            });
        }
        internal static void ToggleBottom()
        {
            if (bottom == null) return;
            bool on = !bottom.activeSelf;
            bottom.SetActive(on);
            if (on) DrawBottom();
            else BottomHover(null);
        }

        static void SwitchBottom(int player)
        {
            if (bottomPlayer == player) return;
            bottomPlayer = player;
            bottomKey = null;
            if (bottomContent != null) bottomContent.anchoredPosition = Vector2.zero;
            DrawBottom();
        }

        static void PlaceBottom(out float w, out float h, out float full)
        {
            var box = (RectTransform)bottom.transform;
            var parent = (RectTransform)box.parent;
            if (game != null && game.go_DownloadLog != null && game.go_ChoiceButton2 != null)
            {
                var dl = InParent(parent, (RectTransform)game.go_DownloadLog.transform);
                var ch2 = InParent(parent, (RectTransform)game.go_ChoiceButton2.transform);
                if (dl.width > 1f && dl.height > 1f && ch2.height > 1f)
                {
                    float bw = dl.width, gap = bw * 0.06f;
                    float right = dl.xMin - bw * 0.1f, top = dl.yMax, narrowest = bw * 1.3f;
                    if (Time.realtimeSinceStartup >= bottomNextTable || Screen.width != bottomTableW || Screen.height != bottomTableH)
                    {
                        bottomNextTable = Time.realtimeSinceStartup + 0.5f;
                        bottomTableW = Screen.width;
                        bottomTableH = Screen.height;
                        bottomTableEdge = TableEdge(parent, right - narrowest - gap);
                    }
                    float left = Mathf.Max(right - bw * 1.8f, bottomTableEdge + gap);
                    left = right - Mathf.Floor((right - left) / 2) * 2;
                    w = right - left;
                    float bottomY = ch2.yMax + gap;
                    full = Mathf.Max(top - bottomY, w * 0.75f);
                    var guide = game.text_GuideText;
                    if (guide != null && guide.isActiveAndEnabled && !string.IsNullOrEmpty(guide.text) && guide.textBounds.size.y > 0)
                    {
                        var b = guide.textBounds;
                        var r = InParent(parent, guide.rectTransform.TransformPoint(b.min), guide.rectTransform.TransformPoint(b.max));
                        if (r.height <= parent.rect.height * 0.25f && r.xMax > left && r.xMin < right && r.yMin < top && r.yMax > bottomY)
                            bottomY = Mathf.Max(bottomY, r.yMax + gap);
                    }
                    h = Mathf.Floor((top - bottomY) / 4) * 4;
                    if (w >= 60f && h >= w * 0.75f)
                    {
                        box.localPosition = new Vector3(left, top - h, 0);
                        box.sizeDelta = new Vector2(w, h);
                        return;
                    }
                }
            }
            var pr = parent.rect;
            w = Mathf.Floor(pr.width * 0.16f / 2) * 2;
            h = Mathf.Floor(pr.height * 0.62f / 4) * 4;
            float fx = pr.xMin + pr.width * 0.73f;
            float fTop = pr.yMax - pr.height * 0.14f;
            full = Mathf.Max(h, w * 0.75f);
            box.localPosition = new Vector3(fx, fTop - h, 0);
            box.sizeDelta = new Vector2(w, h);
        }

        static float TableEdge(RectTransform parent, float limit)
        {
            float edge = float.MinValue;
            void Add(GameObject go)
            {
                if (go == null || !go.activeInHierarchy || !(go.transform is RectTransform rt)) return;
                rt.GetWorldCorners(bottomCorners);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    var s = RectTransformUtility.WorldToScreenPoint(bottomCam, bottomCorners[i]);
                    x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
                }
                if (x0 < 0 || y0 < 0 || x1 > Screen.width || y1 > Screen.height) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(x1, y1), bottomCam, out var local);
                if (local.x <= limit) edge = Mathf.Max(edge, local.x);
            }
            Add(game.go_P0Board);
            Add(game.go_P1Board);
            if (game.Lps_Players != null)
                foreach (var p in game.Lps_Players)
                    foreach (var list in new[] { p.Lgo_MyDeck, p.Lgo_MyDonDeck, p.Lgo_MyLifeDeck })
                        if (list != null) foreach (var c in list) Add(c);
            return edge;
        }

        static void DrawBottom()
        {
            var m = ReplayRoom.Board;
            if (m == null || bottom == null) return;
            PlaceBottom(out float w, out float h, out float full);
            var groups = m.BottomGroups(bottomPlayer);
            string key = $"{bottomPlayer}|{Mathf.RoundToInt(w)}x{Mathf.RoundToInt(h)}x{Mathf.RoundToInt(full)}|" +
                string.Join(";", groups.Select(g => string.Join(",", g.Select(pc => pc.bottomCovered ? "?" : (pc.card ?? "?")))));
            if (key == bottomKey) return;
            bottomKey = key;

            var box = (RectTransform)bottom.transform;
            BottomHover(null);
            foreach (Transform t in box) if (t != bottomViewport && t.name != "Bar") { t.gameObject.SetActive(false); UnityEngine.Object.Destroy(t.gameObject); }
            foreach (Transform t in bottomContent) { t.gameObject.SetActive(false); UnityEngine.Object.Destroy(t.gameObject); }
            bottomCells.Clear();

            float pad = w * 0.055f, headH = w * 0.225f, footH = w * 0.15f, barW = w * 0.025f;
            string titleText = bottomPlayer == 1 ? "DECK\nBOTTOM" : "OPP DECK\nBOTTOM";
            var title = BottomText(box, titleText, pad, pad * 0.55f, w * 0.48f, headH - pad * 0.8f, w * 0.066f, TextAlignmentOptions.Left, Color.white);
            title.characterSpacing = 4;

            float btnY = pad * 0.55f, btnH = w * 0.082f, btnGap = w * 0.016f;
            float closeW = w * 0.088f, sideW = w * 0.135f;
            float closeX = w - pad - closeW;
            float oppX = closeX - btnGap - sideW;
            float youX = oppX - btnGap - sideW;
            HeaderButton(box, "BottomYou", "You", youX, btnY, sideW, btnH, w * 0.04f,
                bottomPlayer == 1 ? Gold : PillOff, bottomPlayer == 1 ? Edge : Soft, () => SwitchBottom(1));
            HeaderButton(box, "BottomOpp", "Opp", oppX, btnY, sideW, btnH, w * 0.04f,
                bottomPlayer == 2 ? Gold : PillOff, bottomPlayer == 2 ? Edge : Soft, () => SwitchBottom(2));
            HeaderButton(box, "BottomClose", "X", closeX, btnY, closeW, btnH, w * 0.042f,
                PillOff, Color.white, ToggleBottom);

            float subY = btnY + btnH + w * 0.01f, subH = headH - subY - pad * 0.2f;
            BottomText(box, "left = you draw it first", w * 0.44f, subY, w * 0.56f - pad, subH, w * 0.042f, TextAlignmentOptions.Right, Soft).fontStyle = FontStyles.Normal;

            float listY = headH, listH = h - headH - footH;
            SetRect(bottomViewport, pad, listY, w - 2 * pad - barW * 1.6f, listH);
            SetRect((RectTransform)bottomScroll.verticalScrollbar.transform, w - pad - barW, listY, barW, listH);

            var line = NewRect(box, "Line", typeof(Image));
            SetRect(line, pad, h - footH, w - 2 * pad, Mathf.Max(2, w * 0.008f));
            Paint(line.gameObject, Gold);
            int count = 0;
            foreach (var g in groups) count += g.Count;
            BottomText(box, "TOTAL UNDER", pad, h - footH, w * 0.6f, footH, w * 0.06f, TextAlignmentOptions.Left, Color.white).characterSpacing = 3;
            BottomText(box, count.ToString(), w * 0.6f, h - footH, w * 0.4f - pad, footH, w * 0.085f, TextAlignmentOptions.Right, Gold);

            float viewW = w - 2 * pad - barW * 1.6f, numW = w * 0.11f, gap = w * 0.016f, rowPad = w * 0.022f;
            if (groups.Count == 0)
            {
                bottomContent.sizeDelta = new Vector2(0, listH);
                bottomContent.anchoredPosition = Vector2.zero;
                BottomText(bottomContent, "Nothing under yet", 0, 0, viewW, w * 0.2f, w * 0.05f, TextAlignmentOptions.Left, new Color(1, 1, 1, 0.45f)).fontStyle = FontStyles.Normal;
                return;
            }

            float twMax = (viewW - numW - 3 * gap) / 4, twMin = twMax * 0.7f;
            int rows = 0;
            float fixedH = 0;
            foreach (var g in groups)
            {
                int r = (g.Count + 3) / 4;
                rows += r;
                fixedH += 2 * rowPad + (r - 1) * gap;
            }
            float tw = Mathf.Clamp((full - headH - footH - fixedH) / Mathf.Max(1, rows) / CardRatio, twMin, twMax), th = tw * CardRatio;
            bottomStep = th;
            var back = CardBack(bottomPlayer);

            float y = 0;
            for (int n = 0; n < groups.Count; n++)
            {
                var g = groups[n];
                int r = (g.Count + 3) / 4;
                float blockH = 2 * rowPad + r * th + (r - 1) * gap;
                var sep = NewRect(bottomContent, "Sep", typeof(Image));
                SetRect(sep, 0, y, viewW, 1);
                Paint(sep.gameObject, new Color(1, 1, 1, 0.13f));
                BottomText(bottomContent, (n + 1).ToString(), 0, y, numW, blockH, w * 0.075f, TextAlignmentOptions.Center, Gold);
                for (int i = 0; i < g.Count; i++)
                {
                    var pc = g[i];
                    bool covered = pc.bottomCovered || string.IsNullOrEmpty(pc.card);
                    var id = covered ? null : pc.card;
                    var cell = NewRect(bottomContent, id ?? "?", typeof(Image));
                    SetRect(cell, numW + (i % 4) * (tw + gap), y + rowPad + (i / 4) * (th + gap), tw, th);
                    var img = cell.GetComponent<Image>();
                    img.raycastTarget = false;
                    var sprite = covered ? back : CardSprite(id);
                    if (sprite != null) { img.sprite = sprite; img.preserveAspect = !covered; }
                    else
                    {
                        img.color = new Color(0.2f, 0.24f, 0.35f);
                        BottomText(cell, id ?? "?", 0, 0, tw, th, tw * 0.16f, TextAlignmentOptions.Center, Color.white);
                    }
                    if (id != null) bottomCells.Add((cell, id));
                }
                y += blockH;
            }
            bottomContent.sizeDelta = new Vector2(0, y);
            float most = Mathf.Max(0, y - listH);
            bottomContent.anchoredPosition = new Vector2(0, Mathf.Clamp(bottomContent.anchoredPosition.y, 0, most));
        }

        static void BottomMouse()
        {
            if (bottomViewport == null) return;
            Vector2 m = Input.mousePosition;
            float wheel = Input.mouseScrollDelta.y;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(bottomViewport, m, bottomCam);
            if (inside && wheel != 0 && bottomStep > 0)
            {
                float most = Mathf.Max(0, bottomContent.rect.height - bottomViewport.rect.height);
                bottomContent.anchoredPosition = new Vector2(0, Mathf.Clamp(bottomContent.anchoredPosition.y - wheel * bottomStep, 0, most));
            }
            string id = null;
            if (inside)
                foreach (var (rt, cid) in bottomCells)
                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, m, bottomCam)) { id = cid; break; }
            BottomHover(id);
        }

        static void BottomHover(string id)
        {
            if (id == bottomHovering) return;
            bottomHovering = id;
            if (game != null) game.FocusCardInChat(id ?? "");
        }

        static Sprite CardBack(int player)
        {
            if (game == null || game.Lps_Players == null || game.Lps_Players.Count < player) return null;
            var ps = game.Lps_Players[player - 1];
            foreach (var list in new[] { ps.Lgo_MyDeck, ps.Lgo_MyHand, ps.Lgo_MyLeader, ps.Lgo_MyDeploy, ps.Lgo_MyTrash, ps.Lgo_MyLifeDeck })
            {
                if (list == null) continue;
                foreach (var c in list)
                {
                    var cls = c != null ? c.GetComponent<CardLogicScript>() : null;
                    var lib = cls != null ? cls.sc_ImageLibrary : null;
                    if (lib != null) return player == 1 && lib.CardBackMine != null ? lib.CardBackMine : lib.CardBackRegular;
                }
            }
            return null;
        }

        static Sprite CardSprite(string id)
        {
            try { return game != null && game.card_Database != null && game.card_Database.dict_Cards.ContainsKey(id) ? game.card_Database.GetCardImage(id, SpriteState.Thumbnail) : null; }
            catch (Exception) { return null; }
        }

        static Rect InParent(RectTransform parent, RectTransform rt)
        {
            rt.GetWorldCorners(bottomCorners);
            return InParent(parent, bottomCorners[0], bottomCorners[2]);
        }

        static Rect InParent(RectTransform parent, Vector3 worldA, Vector3 worldB)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(bottomCam, worldA), bottomCam, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(bottomCam, worldB), bottomCam, out var b);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static void Paint(GameObject go, Color color)
        {
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        static RectTransform NewRect(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            foreach (var type in components) go.AddComponent(type);
            return rt;
        }

        static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static TextMeshProUGUI BottomText(RectTransform parent, string text, float x, float y, float w, float h, float size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (bottomFont != null && bottomFont.font != null)
            {
                t.font = bottomFont.font;
                if (bottomTextMat == null)
                {
                    bottomTextMat = new Material(bottomFont.font.material) { name = "OPTCGTrainer bottom text" };
                    bottomTextMat.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
                }
                t.fontSharedMaterial = bottomTextMat;
            }
            SetRect(t.rectTransform, x, y, w, h);
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.richText = true;
            t.raycastTarget = false;
            t.verticalAlignment = VerticalAlignmentOptions.Middle;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static void HeaderButton(RectTransform parent, string name, string label, float x, float y, float w, float h, float size, Color bg, Color fg, Action click)
        {
            var rt = NewRect(parent, name, typeof(Image), typeof(Button));
            SetRect(rt, x, y, w, h);
            var img = rt.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = rt.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                click();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });
            BottomText(rt, label, 0, 0, w, h, size, TextAlignmentOptions.Center, fg);
        }

        static void LoadText()
        {
            entries.TryGetValue(moment,out var entry);
            input.SetTextWithoutNotify(entry?.text ?? "");
            noteTitle.text = "Move notes · " + ReplayRoom.Status(false);
            noteStatus.text = canSave ? (entries.Count == 0 ? "No saved moments" : $"Saved moments: {entries.Count}") : "Notes unavailable";
        }
        static bool SaveDraft()
        {
            entries.TryGetValue(moment,out var entry);
            return input.text == (entry?.text ?? "") || Save(false);
        }
        static bool Save(bool drawing)
        {
            if (!canSave) { noteStatus.text = penTitle.text = "Notes unavailable"; return false; }
            var updated = new SortedDictionary<int,Entry>(entries);
            entries.TryGetValue(moment,out var old);
            var e = new Entry { text = drawing ? old?.text : input.text, strokes = drawing ? ink.Snapshot() : old?.strokes };
            if (string.IsNullOrWhiteSpace(e.text) && (e.strokes == null || e.strokes.Count == 0)) updated.Remove(moment);
            else updated[moment] = e;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file+".tmp", JsonConvert.SerializeObject(updated));
                if (File.Exists(file)) File.Replace(file+".tmp",file,null); else File.Move(file+".tmp",file);
                entries = updated; NoteVersion++; noteStatus.text = "Saved"; return true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Replay note: "+ex.Message); noteStatus.text = penTitle.text = "Could not save"; return false; }
        }
        static void GoNote(int dir)
        {
            if (!SaveDraft()) return;
            var targets = dir > 0 ? entries.Keys.Where(k => k>moment) : entries.Keys.Reverse().Where(k => k<moment);
            foreach (int target in targets) { ReplayRoom.Seek(target); Refresh(); return; }
            noteStatus.text = dir > 0 ? "No later note" : "No earlier note";
        }
        internal static void Refresh()
        {
            if (root == null) return;
            if (shareBanner != null && shareBanner.activeSelf && Time.realtimeSinceStartup > shareBannerUntil)
                shareBanner.SetActive(false);
            if (moment != ReplayRoom.Moment)
            {
                if (notes.activeSelf && moment != int.MinValue) SaveDraft();
                moment = ReplayRoom.Moment;
                entries.TryGetValue(moment,out var entry);
                ink.Load(entry?.strokes);
                if (notes.activeSelf) LoadText();
            }
            if (bottom != null && bottom.activeSelf)
            {
                DrawBottom();
                BottomMouse();
            }
            else BottomHover(null);
        }
        internal static bool Over(Vector2 p)
        {
            if (root == null) return false;
            return (notes != null && Look.Contains((RectTransform)notes.transform, p))
                || (bottom != null && Look.Contains((RectTransform)bottom.transform, p))
                || (marker != null && Look.Contains((RectTransform)marker.transform, p))
                || (shareBanner != null && shareBanner.activeSelf && Look.Contains((RectTransform)shareBanner.transform, p))
                || (ink != null && ink.editing && Look.Contains((RectTransform)ink.transform, p));
        }
        internal static void Close()
        {
            if (root == null) return;
            shareRequestToken++;
            shareBannerUntil = 0;
            wasNotes = wasMarker = wasBottom = wasBanner = false;
            BottomHover(null);
            if (notes != null && notes.activeSelf && input != null && noteStatus != null) SaveDraft();
            root.SetActive(false); UnityEngine.Object.Destroy(root);
            if (bottomTextMat != null) { UnityEngine.Object.Destroy(bottomTextMat); bottomTextMat = null; }
            root = notes = marker = bottom = shareBanner = null;
            shareBannerText = null;
            bottomViewport = bottomContent = null; bottomScroll = null; bottomFont = null; bottomCam = null;
            bottomCells.Clear(); bottomKey = null; bottomHovering = null;
            ink = null; input = null; entries = null;
            NoteVersion++;
        }
    }
}
