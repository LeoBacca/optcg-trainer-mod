using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    // Replay Room Match Library + "Watch Shared Replay by Link" hub (1920x991 CoachLook overlay).
    static class ReplayLibrary
    {
        const int PageSize = 6;
        const int LeaderPageSize = 6;
        const string DefaultHint = "Paste a link (?r=...) or 10-char code";

        enum FilterKind { All, Wins, Losses, Notes, Shared }

        sealed class ReplayEntry
        {
            public string path, fileName, id, sha256;
            public List<FileInfo> localCopies;
            public DateTime date;
            public bool hasTime, fromSharedDir, isSolo, hasNotes, isShared;
            public string result; // "w", "l", "o"
            public int turns, first;
            public string myLeader, oppLeader, myLeaderDisplay, oppLeaderDisplay;
            public string myName, oppName, kind, format;
        }

        static readonly Color BadgeWin = Hex("#2ea043");
        static readonly Color BadgeLoss = Hex("#d94436");
        static readonly Color GoldTintBg = new Color(0.96f, 0.76f, 0.26f, 0.20f);
        static readonly Color SharedTintBg = new Color(0.29f, 0.50f, 0.88f, 0.24f);

        static GameObject source, entry, shade;
        static RectTransform pageRoot, bodyLayer, popupLayer;
        static TextMeshProUGUI statsText, statusText, filterBtnText;
        static Image filterBtnBg;
        static Outline filterBtnEdge;
        static TMP_InputField linkInput;
        static Material inputTextMat;

        static readonly List<ReplayEntry> allEntries = new List<ReplayEntry>();
        static readonly List<ReplayEntry> filtered = new List<ReplayEntry>();
        static readonly List<string> distinctLeaders = new List<string>();
        static FilterKind activeFilter = FilterKind.All;
        static string leaderFilter;
        static bool filterPopupOpen;
        static int leaderPopupPage;
        static string draftLink = "";
        static string statusMessage;
        static bool statusIsError;
        static int page;
        static int opToken;
        static float nextCheck, nextWheel;

        internal static bool Visible => shade != null;

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        internal static void FollowMenu()
        {
            if (entry == null || source == null)
            {
                if (Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + 0.5f;
                    var host = UnityEngine.Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                    source = host != null ? host.go_Patchnotes : null;
                    if (source != null && source.activeInHierarchy)
                    {
                        var canvas = source.GetComponentInParent<Canvas>(true);
                        if (canvas != null)
                            entry = MakeMenuButton(canvas.rootCanvas.transform, "ReplayLibrary", "Replay Room", Show, 0.77f, 0.22f, 0.16f, 0.055f);
                    }
                }
            }
            if (entry != null && source != null)
            {
                bool visible = source.activeInHierarchy && !ReplayRoom.Active;
                if (entry.activeSelf != visible) entry.SetActive(visible);
                if (Visible && !ReplayRoom.Active && !visible) { Close(); return; }
            }
            if (!Visible) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (popupLayer != null) CloseFilterPopup();
                else Close();
                return;
            }

            if (popupLayer != null) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.05f && Time.unscaledTime >= nextWheel)
            {
                int pages = PageCount();
                if (wheel < 0 && page + 1 < pages) { page++; nextWheel = Time.unscaledTime + 0.12f; DrawBody(); }
                else if (wheel > 0 && page > 0) { page--; nextWheel = Time.unscaledTime + 0.12f; DrawBody(); }
            }
        }

        static GameObject MakeMenuButton(Transform parent, string name, string label, Action click, float x, float y, float w, float h)
        {
            var button = Look.CloneButton(source, name, label, Look.Blue, () => click());
            button.transform.SetParent(parent, false);
            Look.Place((RectTransform)button.transform, x, y, w, h);
            var t = button.GetComponentInChildren<TMP_Text>(true);
            if (t != null)
            {
                t.richText = false;
                t.alignment = TextAlignmentOptions.Center;
                t.enableAutoSizing = true; t.fontSizeMin = 8; t.fontSizeMax = 28;
                t.margin = Vector4.zero;
            }
            return button;
        }

        internal static void Show()
        {
            if (Visible) return;
            if (source == null)
            {
                var host = UnityEngine.Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                source = host != null ? (host.go_Patchnotes ?? host.go_Settings) : null;
            }
            var fontFrom = source != null ? source.GetComponentInChildren<TMP_Text>(true) : null;
            if (fontFrom == null) fontFrom = UnityEngine.Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);
            if (fontFrom != null) CoachLook.UseFont(fontFrom);

            ReplayRoom.Pause();
            statusMessage = null;
            statusIsError = false;
            filterPopupOpen = false;
            leaderPopupPage = 0;
            page = 0;

            shade = new GameObject("ReplayLibraryOverlay", typeof(RectTransform));
            var canvas = shade.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001;
            var scaler = shade.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CoachLook.W, CoachLook.H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            shade.AddComponent<GraphicRaycaster>();

            // Darkened clickable backdrop covering screen
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            var bgrt = (RectTransform)bgGo.transform;
            bgrt.SetParent(shade.transform, false);
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = bgrt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = new Color(0.025f, 0.045f, 0.09f, 0.80f);
            bgImg.raycastTarget = true;
            bgGo.AddComponent<Hover>().click = Close;

            pageRoot = (RectTransform)new GameObject("Page", typeof(RectTransform)).transform;
            pageRoot.SetParent(shade.transform, false);
            pageRoot.anchorMin = pageRoot.anchorMax = pageRoot.pivot = new Vector2(0.5f, 0.5f);
            pageRoot.sizeDelta = new Vector2(CoachLook.W, CoachLook.H);

            // Central panel (120, 48, 1680, 896)
            CoachLook.Box(pageRoot, 120, 48, 1680, 896, CoachLook.Panel, 8, clicks: true);
            CoachLook.Frame(pageRoot, 120, 48, 1680, 896, CoachLook.PanelLine, 2);

            BuildTopChrome();
            ScanEntries();
            UpdateHeaderStats();
            SyncFilterButton();
            DrawBody();
        }

        internal static void Close()
        {
            opToken++;
            filterPopupOpen = false;
            leaderPopupPage = 0;
            if (linkInput != null) { draftLink = linkInput.text ?? ""; linkInput.DeactivateInputField(); }
            if (shade != null) { shade.SetActive(false); UnityEngine.Object.Destroy(shade); }
            shade = null;
            pageRoot = bodyLayer = popupLayer = null;
            statsText = statusText = filterBtnText = null;
            filterBtnBg = null;
            filterBtnEdge = null;
            linkInput = null;
            if (inputTextMat != null) { UnityEngine.Object.Destroy(inputTextMat); inputTextMat = null; }
        }

        internal static void OpenShared(string linkOrId)
        {
            string trimmed = (linkOrId ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                SetStatus("Paste an OPTCG Trainer replay link (?r=...) or 10-char code first.", true);
                return;
            }
            int token = ++opToken;
            SetStatus("Downloading shared replay...", false);
            try
            {
                Account.DownloadReplay(trimmed, (ok, savedPath, msg) =>
                {
                    if (token != opToken) return;
                    if (!ok || string.IsNullOrEmpty(savedPath))
                    {
                        string err = string.IsNullOrEmpty(msg) ? "Could not download shared replay." : msg;
                        SetStatus(err, true);
                        Plugin.Log.LogWarning("ReplayLibrary.OpenShared: " + err);
                        return;
                    }
                    try
                    {
                        if (ReplayRoom.Active) ReplayRoom.Exit();
                        string res = ReplayRoom.Open(savedPath);
                        if (ReplayRoom.Active) Close();
                        else SetStatus(string.IsNullOrEmpty(res) ? "Could not open downloaded replay." : res, true);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("ReplayLibrary.OpenShared open failed: " + ex);
                        SetStatus("Could not open replay: " + ex.Message, true);
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ReplayLibrary.OpenShared failed: " + ex);
                SetStatus("Could not download replay: " + ex.Message, true);
            }
        }

        // ---- Top chrome: Header + Watch Shared Replay & Filter bar ----

        static void BuildTopChrome()
        {
            // Header (y: 62..120)
            CoachLook.Write(pageRoot, 156, 58, 1280, 38,
                "REPLAY ROOM  <color=" + Look.GoldHex + ">· MATCH LIBRARY</color>",
                30, CoachLook.Text, true, TextAlignmentOptions.TopLeft, true);
            statsText = CoachLook.Write(pageRoot, 156, 95, 1280, 30, "", 20, CoachLook.Soft);

            UiButton(pageRoot, "LibraryRefresh", 1506, 72, 124, 46, "REFRESH",
                CoachLook.Plan, CoachLook.Outline, CoachLook.Text, () =>
                {
                    CloseFilterPopup();
                    ScanEntries();
                    UpdateHeaderStats();
                    SyncFilterButton();
                    DrawBody();
                    SetStatus("Library refreshed (" + allEntries.Count + (allEntries.Count == 1 ? " match)." : " matches)."), false);
                }, 18);

            UiButton(pageRoot, "LibraryClose", 1642, 72, 122, 46, "CLOSE",
                CoachLook.Card, CoachLook.CardLine, CoachLook.Soft, Close, 18);

            CoachLook.Box(pageRoot, 156, 124, 1608, 1, CoachLook.CardLine);

            // Top Bar (y: 132..208): Watch Shared Replay + Dropdown Filter button
            CoachLook.Box(pageRoot, 156, 132, 1608, 76, CoachLook.Card, 6, clicks: true);
            CoachLook.Frame(pageRoot, 156, 132, 1608, 76, CoachLook.CardLine, 1);

            CoachLook.Caps(pageRoot, 174, 142, 356, "WATCH SHARED REPLAY", 15);
            statusText = CoachLook.Write(pageRoot, 174, 169, 356, 28, DefaultHint, 16, CoachLook.Label);
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            RefreshStatusText();

            BuildLinkInput(542, 147, 616, 46);

            UiButton(pageRoot, "OpenSharedLink", 1170, 147, 164, 46, "OPEN LINK",
                CoachLook.Ready, CoachLook.ReadyLine, Color.white, () =>
                {
                    CloseFilterPopup();
                    string raw = linkInput != null ? linkInput.text : draftLink;
                    draftLink = raw ?? "";
                    OpenShared(raw);
                }, 18);

            var filterBtn = UiButton(pageRoot, "FilterToggleBtn", 1356, 147, 390, 46, FilterButtonLabel(),
                CoachLook.Plan, CoachLook.Outline, CoachLook.Text, ToggleFilterPopup, 17);
            filterBtnBg = filterBtn.GetComponent<Image>();
            filterBtnEdge = filterBtn.GetComponent<Outline>();
            filterBtnText = filterBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        static void BuildLinkInput(float x, float y, float w, float h)
        {
            CoachLook.Box(pageRoot, x, y, w, h, CoachLook.Tip, 5, clicks: false);
            CoachLook.Frame(pageRoot, x, y, w, h, CoachLook.Outline, 1);

            var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>(FindObjectsInactive.Include);
            var template = game != null ? game.go_ChatBox : null;
            if (template == null || template.GetComponent<TMP_InputField>() == null)
            {
                var host = UnityEngine.Object.FindAnyObjectByType<HostJoinScript>(FindObjectsInactive.Include);
                template = host != null ? host.go_PlayerName : null;
            }
            if (template == null || template.GetComponent<TMP_InputField>() == null)
            {
                CoachLook.Write(pageRoot, x + 12, y + 10, w - 24, h - 20,
                    "Enter link (?r=...) or 10-char code", 18, CoachLook.Dim);
                return;
            }

            var copy = Look.SafeCopy(template, pageRoot);
            copy.name = "SharedReplayInput";
            CoachLook.Put((RectTransform)copy.transform, x + 10, y + 2, w - 20, h - 4);
            var input = copy.GetComponent<TMP_InputField>();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 256;
            input.caretColor = Color.white;
            input.selectionColor = new Color(0.29f, 0.50f, 0.88f, 0.45f);
            input.text = draftLink ?? "";
            input.onValueChanged.AddListener(v => draftLink = v ?? "");
            input.onSubmit.AddListener(v => { draftLink = v ?? ""; OpenShared(v); });

            if (copy.GetComponent<Image>() is Image bg) { bg.color = new Color(0, 0, 0, 0); bg.raycastTarget = true; }
            if (input.textComponent != null)
            {
                if (CoachLook.Font != null) input.textComponent.font = CoachLook.Font;
                var mat = InputMaterial(input.textComponent.font);
                if (mat != null) input.textComponent.fontSharedMaterial = mat;
                input.textComponent.fontSize = 20;
                input.textComponent.color = CoachLook.Text;
                input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
                input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
                input.textComponent.overflowMode = TextOverflowModes.Overflow;
                input.textComponent.margin = Vector4.zero;
            }
            if (input.placeholder is TMP_Text ph)
            {
                if (CoachLook.Font != null) ph.font = CoachLook.Font;
                var mat = InputMaterial(ph.font);
                if (mat != null) ph.fontSharedMaterial = mat;
                ph.text = "https://optcgtrainer.com/?r=... or 10-char code";
                ph.fontSize = 18;
                ph.color = CoachLook.Dim;
                ph.alignment = TextAlignmentOptions.MidlineLeft;
                ph.textWrappingMode = TextWrappingModes.NoWrap;
                ph.margin = Vector4.zero;
            }
            copy.SetActive(true);
            linkInput = input;
        }

        static Material InputMaterial(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            if (inputTextMat == null || inputTextMat.shader != font.material.shader)
            {
                if (inputTextMat != null) UnityEngine.Object.Destroy(inputTextMat);
                inputTextMat = new Material(font.material) { name = "OPTCGTrainer library input" };
                inputTextMat.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            }
            return inputTextMat;
        }

        internal static void SetStatus(string msg, bool error)
        {
            statusMessage = msg;
            statusIsError = error;
            RefreshStatusText();
        }

        static void RefreshStatusText()
        {
            if (statusText == null) return;
            if (string.IsNullOrEmpty(statusMessage))
            {
                statusText.text = DefaultHint;
                statusText.color = CoachLook.Label;
            }
            else
            {
                statusText.text = CoachUi.Safe(statusMessage);
                statusText.color = statusIsError ? CoachLook.Warn : CoachLook.Gold;
            }
        }

        static void UpdateHeaderStats()
        {
            if (statsText == null) return;
            int total = allEntries.Count;
            statsText.text = total == 0 ? "No replays found" : total == 1 ? "1 match" : total + " matches";
        }

        // ---- Dropdown Filter Popup ----

        static string FilterButtonLabel()
        {
            string kindPart = activeFilter switch
            {
                FilterKind.Wins => "WINS",
                FilterKind.Losses => "LOSSES",
                FilterKind.Notes => "NOTES",
                FilterKind.Shared => "SHARED",
                _ => "ALL"
            };
            string arrow = filterPopupOpen ? "▲" : "▼";
            if (leaderFilter == null) return $"FILTER: {kindPart}  {arrow}";
            string leaderShort = ShortLeaderTag(leaderFilter);
            return activeFilter == FilterKind.All
                ? $"FILTER: {leaderShort}  {arrow}"
                : $"FILTER: {kindPart} · {leaderShort}  {arrow}";
        }

        static string ShortLeaderTag(string leaderId)
        {
            string full = CoachUi.LeaderName(leaderId, null);
            var parts = (full ?? leaderId ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string tag = parts.Length > 0 ? parts[parts.Length - 1] : leaderId;
            return Short(tag, 12).ToUpperInvariant();
        }

        static void SyncFilterButton()
        {
            bool filteredNow = activeFilter != FilterKind.All || leaderFilter != null;
            if (filterBtnText != null)
            {
                filterBtnText.text = FilterButtonLabel();
                filterBtnText.color = filteredNow || filterPopupOpen ? CoachLook.Gold : CoachLook.Text;
            }
            if (filterBtnBg != null) filterBtnBg.color = filteredNow ? CoachLook.RestB : CoachLook.Plan;
            if (filterBtnEdge != null) filterBtnEdge.effectColor = filteredNow || filterPopupOpen ? CoachLook.Gold : CoachLook.Outline;
        }

        static void ToggleFilterPopup()
        {
            if (filterPopupOpen) CloseFilterPopup();
            else
            {
                filterPopupOpen = true;
                SyncFilterButton();
                DrawFilterPopup();
            }
        }

        static void CloseFilterPopup()
        {
            filterPopupOpen = false;
            if (popupLayer != null) { popupLayer.gameObject.SetActive(false); UnityEngine.Object.Destroy(popupLayer.gameObject); popupLayer = null; }
            SyncFilterButton();
        }

        static void DrawFilterPopup()
        {
            if (pageRoot == null) return;
            if (popupLayer != null) { popupLayer.gameObject.SetActive(false); UnityEngine.Object.Destroy(popupLayer.gameObject); popupLayer = null; }
            if (!filterPopupOpen) return;

            popupLayer = CoachLook.At(pageRoot, "FilterPopupLayer", 0, 0, CoachLook.W, CoachLook.H);
            popupLayer.SetAsLastSibling();

            // Invisible click-outside catcher behind the popup card
            CoachLook.Hot(popupLayer, 0, 0, CoachLook.W, CoachLook.H, CloseFilterPopup);

            int leaderPages = Math.Max(1, (distinctLeaders.Count + LeaderPageSize - 1) / LeaderPageSize);
            if (leaderPopupPage >= leaderPages) leaderPopupPage = leaderPages - 1;
            if (leaderPopupPage < 0) leaderPopupPage = 0;

            int visibleLeaders = Math.Min(LeaderPageSize, Math.Max(0, distinctLeaders.Count - leaderPopupPage * LeaderPageSize));
            bool showLeaderPager = distinctLeaders.Count > LeaderPageSize;

            const float px = 1356, py = 199, pw = 390;
            const float pad = 14, rowH = 38, rowGap = 6;
            float ph = pad + 24 + 5 * (rowH + rowGap) + 12 + 24 + (1 + visibleLeaders) * (rowH + rowGap)
                + (showLeaderPager ? 40 : 0) + pad;

            CoachLook.Box(popupLayer, px, py, pw, ph, CoachLook.Panel, 6, clicks: true);
            CoachLook.Frame(popupLayer, px, py, pw, ph, CoachLook.Gold, 2);

            float y = py + pad;
            CoachLook.Caps(popupLayer, px + pad, y, pw - pad * 2, "MATCH TYPE", 13);
            y += 24;

            y = PopupFilterOption(px + pad, y, pw - pad * 2, rowH, "All matches", FilterKind.All) + rowGap;
            y = PopupFilterOption(px + pad, y, pw - pad * 2, rowH, "Wins only", FilterKind.Wins) + rowGap;
            y = PopupFilterOption(px + pad, y, pw - pad * 2, rowH, "Losses only", FilterKind.Losses) + rowGap;
            y = PopupFilterOption(px + pad, y, pw - pad * 2, rowH, "With notes", FilterKind.Notes) + rowGap;
            y = PopupFilterOption(px + pad, y, pw - pad * 2, rowH, "Shared links", FilterKind.Shared) + rowGap;

            y += 4;
            CoachLook.Box(popupLayer, px + pad, y, pw - pad * 2, 1, CoachLook.CardLine);
            y += 8;

            string leaderCap = showLeaderPager ? $"MY LEADER  ({leaderPopupPage + 1}/{leaderPages})" : "MY LEADER";
            CoachLook.Caps(popupLayer, px + pad, y, pw - pad * 2, leaderCap, 13);
            y += 24;

            y = PopupLeaderOption(px + pad, y, pw - pad * 2, rowH, "All leaders", null) + rowGap;

            int lStart = leaderPopupPage * LeaderPageSize;
            for (int i = 0; i < visibleLeaders; i++)
            {
                string lid = distinctLeaders[lStart + i];
                string label = Short(CoachUi.LeaderName(lid, null), 20) + "  (" + lid + ")";
                y = PopupLeaderOption(px + pad, y, pw - pad * 2, rowH, label, lid) + rowGap;
            }

            if (showLeaderPager)
            {
                float halfW = (pw - pad * 2 - 8) * 0.5f;
                bool canPrev = leaderPopupPage > 0;
                bool canNext = leaderPopupPage + 1 < leaderPages;
                UiButton(popupLayer, "LeaderPagePrev", px + pad, y + 2, halfW, 34, "< Prev leaders",
                    canPrev ? CoachLook.Plan : CoachLook.Card,
                    canPrev ? CoachLook.Outline : CoachLook.CardLine,
                    canPrev ? CoachLook.Text : CoachLook.Dim,
                    () => { if (leaderPopupPage > 0) { leaderPopupPage--; DrawFilterPopup(); } }, 14, true, canPrev);
                UiButton(popupLayer, "LeaderPageNext", px + pad + halfW + 8, y + 2, halfW, 34, "Next leaders >",
                    canNext ? CoachLook.Plan : CoachLook.Card,
                    canNext ? CoachLook.Outline : CoachLook.CardLine,
                    canNext ? CoachLook.Text : CoachLook.Dim,
                    () => { if (leaderPopupPage + 1 < leaderPages) { leaderPopupPage++; DrawFilterPopup(); } }, 14, true, canNext);
            }
        }

        static float PopupFilterOption(float x, float y, float w, float h, string label, FilterKind kind)
        {
            bool selected = activeFilter == kind;
            UiButton(popupLayer, "PopupFilter_" + kind, x, y, w, h, label,
                selected ? CoachLook.Ready : CoachLook.Card,
                selected ? CoachLook.ReadyLine : CoachLook.CardLine,
                selected ? Color.white : CoachLook.Text,
                () =>
                {
                    activeFilter = kind;
                    page = 0;
                    CloseFilterPopup();
                    DrawBody();
                }, 16, selected, true, TextAlignmentOptions.MidlineLeft);
            return y + h;
        }

        static float PopupLeaderOption(float x, float y, float w, float h, string label, string lid)
        {
            bool selected = lid == null
                ? leaderFilter == null
                : string.Equals(leaderFilter, lid, StringComparison.OrdinalIgnoreCase);
            UiButton(popupLayer, lid == null ? "PopupLeader_All" : "PopupLeader_" + lid, x, y, w, h, label,
                selected ? CoachLook.RestB : CoachLook.Card,
                selected ? CoachLook.Gold : CoachLook.CardLine,
                selected ? CoachLook.Gold : CoachLook.Text,
                () =>
                {
                    leaderFilter = lid;
                    page = 0;
                    CloseFilterPopup();
                    DrawBody();
                }, 15, selected, true, TextAlignmentOptions.MidlineLeft);
            return y + h;
        }

        // ---- Body: Match Rows + Pagination Footer ----

        static void DrawBody()
        {
            if (pageRoot == null) return;
            if (bodyLayer != null) { bodyLayer.gameObject.SetActive(false); UnityEngine.Object.Destroy(bodyLayer.gameObject); }
            bodyLayer = CoachLook.At(pageRoot, "LibraryBody", 0, 0, CoachLook.W, CoachLook.H);
            if (popupLayer != null) popupLayer.SetAsLastSibling();

            ApplyFilters();
            DrawMatchRows();
            DrawPaginationFooter();
        }

        static void DrawMatchRows()
        {
            if (filtered.Count == 0)
            {
                CoachLook.Box(bodyLayer, 156, 260, 1608, 160, CoachLook.Card, 6);
                CoachLook.Frame(bodyLayer, 156, 260, 1608, 160, CoachLook.CardLine, 1);
                string empty = allEntries.Count == 0
                    ? "No replays found yet. Play a match or paste a shared replay link above."
                    : "No replays match the active filter.";
                CoachLook.Write(bodyLayer, 180, 322, 1560, 40, empty, 21, CoachLook.Soft, false, TextAlignmentOptions.Center);
                return;
            }

            int start = page * PageSize;
            for (int row = 0; row < PageSize && start + row < filtered.Count; row++)
                DrawRow(row, filtered[start + row]);
        }

        static void DrawRow(int row, ReplayEntry e)
        {
            float y = 224 + row * 102; // 94px row height + 8px gap -> 224..828
            var rowBg = CoachLook.Box(bodyLayer, 156, y, 1608, 94, CoachLook.Card, 6, clicks: false);
            CoachLook.Frame(bodyLayer, 156, y, 1608, 94, CoachLook.CardLine, 1);

            // Clickable row body underneath action buttons
            CoachLook.Hot(bodyLayer, 156, y, 1608, 94,
                () => OpenEntry(e),
                () => { if (rowBg != null) rowBg.color = CoachLook.Row; },
                () => { if (rowBg != null) rowBg.color = CoachLook.Card; });

            // Left Result Badge (74x74)
            Color chipCol = e.result == "w" ? BadgeWin : e.result == "l" ? BadgeLoss : CoachLook.Unknown;
            string chipText = e.result == "w" ? "WIN" : e.result == "l" ? "LOSS" : "—";
            float chipFont = e.result == "l" ? 21 : 22;
            CoachLook.Box(bodyLayer, 170, y + 10, 74, 74, chipCol, 6);
            CoachLook.Write(bodyLayer, 170, y + 16, 74, 30, chipText, chipFont, Color.white, true, TextAlignmentOptions.Center);
            CoachLook.Write(bodyLayer, 170, y + 49, 74, 22, e.turns > 0 ? "T" + e.turns : "—", 16,
                new Color(1f, 1f, 1f, 0.90f), true, TextAlignmentOptions.Center);

            // My Leader Portrait (54x76) + Name
            DrawPortrait(bodyLayer, 260, y + 9, e.myLeader);
            var myLine = CoachLook.Write(bodyLayer, 326, y + 12, 392, 34,
                FormatLeaderRich(e.myLeaderDisplay, e.myLeader), 23, CoachLook.Text, true);
            myLine.overflowMode = TextOverflowModes.Ellipsis;

            CoachLook.Write(bodyLayer, 724, y + 18, 36, 28, "vs", 19, CoachLook.Dim, true, TextAlignmentOptions.Center);

            // Opp Leader Portrait (54x76) + Name
            DrawPortrait(bodyLayer, 768, y + 9, e.oppLeader);
            var oppLine = CoachLook.Write(bodyLayer, 834, y + 12, 564, 34,
                FormatLeaderRich(e.oppLeaderDisplay, e.oppLeader), 23, CoachLook.Text, true);
            oppLine.overflowMode = TextOverflowModes.Ellipsis;

            // Sub-line (left of Opp portrait: 326..758)
            string when = e.date.ToString(e.hasTime ? "dd MMM HH:mm" : "dd MMM yyyy", CultureInfo.InvariantCulture);
            string players = e.isSolo
                ? "Solo v Self"
                : CoachUi.Safe(Short(string.IsNullOrEmpty(e.myName) ? "You" : e.myName, 14))
                  + " vs "
                  + CoachUi.Safe(Short(string.IsNullOrEmpty(e.oppName) ? "Opponent" : e.oppName, 14));
            var sub = CoachLook.Write(bodyLayer, 326, y + 52, 432, 28, $"{when}  ·  {players}", 18, CoachLook.Soft);
            sub.overflowMode = TextOverflowModes.Ellipsis;

            // Meaningful pills starting at x: 834 (height 28, fontSize 15)
            float px = 834;
            const float maxPx = 1398;
            if (e.first == 1 || e.first == 2)
                px = DrawPill(px, y + 52, 56, e.first == 1 ? "1ST" : "2ND", CoachLook.Plan, CoachLook.CardLine, CoachLook.Soft, maxPx);
            string modeLabel = BuildModeLabel(e.kind, e.format);
            if (!string.IsNullOrEmpty(modeLabel))
            {
                float mw = Mathf.Clamp(modeLabel.Length * 10.5f + 26f, 76f, 200f);
                px = DrawPill(px, y + 52, mw, modeLabel, CoachLook.RestB, CoachLook.Outline, CoachLook.Text, maxPx);
            }
            if (e.hasNotes)
                px = DrawPill(px, y + 52, 88, "NOTES", GoldTintBg, CoachLook.Gold, CoachLook.Gold, maxPx);
            if (e.isShared)
                px = DrawPill(px, y + 52, 96, "SHARED", SharedTintBg, CoachLook.ReadyLine, CoachLook.ReadyLine, maxPx);

            // Right action buttons: Larger WATCH > on LEFT, smaller Share link on RIGHT
            UiButton(bodyLayer, "Watch_" + row, 1412, y + 19, 190, 56, "WATCH >",
                CoachLook.Ready, CoachLook.ReadyLine, Color.white,
                () => OpenEntry(e), 21, true);

            UiButton(bodyLayer, "ShareLink_" + row, 1614, y + 5, 134, 39, "Share link",
                CoachLook.Plan, e.isShared ? CoachLook.ReadyLine : CoachLook.Outline, CoachLook.Soft,
                () => ShareEntry(e), 15, false);
            UiButton(bodyLayer, "Delete_" + row, 1614, y + 50, 134, 39, "Delete",
                CoachLook.Card, CoachLook.CardLine, CoachLook.Soft, () => ConfirmDelete(e), 15, false);
        }

        static void DrawPortrait(Transform parent, float x, float y, string leaderId)
        {
            const float w = 54, h = 76;
            var frame = CoachLook.Box(parent, x, y, w, h, CoachLook.Tip, 3, clicks: false);
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = CoachLook.CardLine;
            outline.effectDistance = new Vector2(1, -1);

            var sprite = LeaderPortrait(leaderId);
            if (sprite != null)
            {
                var img = CoachLook.At(parent, "Portrait", x, y, w, h).gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var fallback = CoachLook.Write(parent, x + 2, y + 4, w - 4, h - 8,
                    CoachUi.Safe(Short(leaderId ?? "?", 10)), 11, CoachLook.Dim, true, TextAlignmentOptions.Center);
                fallback.textWrappingMode = TextWrappingModes.Normal;
                fallback.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        static Sprite LeaderPortrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var db = CardDatabaseScript.Instance;
                if (db == null)
                {
                    var game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>(FindObjectsInactive.Include);
                    db = game != null ? game.card_Database : null;
                }
                if (db != null && db.dict_Cards != null && db.dict_Cards.ContainsKey(id))
                    return db.GetCardImage(id, SpriteState.Thumbnail);
            }
            catch (Exception) { }
            return null;
        }

        static float DrawPill(float x, float y, float w, string label, Color bg, Color border, Color fg, float maxX)
        {
            if (x + w > maxX) return x;
            CoachLook.Box(bodyLayer, x, y, w, 28, bg, 4);
            CoachLook.Frame(bodyLayer, x, y, w, 28, border, 1);
            var t = CoachLook.Write(bodyLayer, x + 4, y, w - 8, 28, CoachUi.Safe(label), 15, fg, true, TextAlignmentOptions.Center);
            t.overflowMode = TextOverflowModes.Ellipsis;
            return x + w + 8;
        }

        static void DrawPaginationFooter()
        {
            CoachLook.Box(bodyLayer, 156, 840, 1608, 1, CoachLook.CardLine);
            int pages = Math.Max(1, PageCount());
            bool canPrev = page > 0;
            bool canNext = page + 1 < pages;

            UiButton(bodyLayer, "PrevPage", 156, 852, 190, 46, "< PREVIOUS",
                canPrev ? CoachLook.Plan : CoachLook.Card,
                canPrev ? CoachLook.Outline : CoachLook.CardLine,
                canPrev ? CoachLook.Text : CoachLook.Dim,
                () => { if (page > 0) { page--; DrawBody(); } }, 18, true, canPrev);

            string summary;
            if (filtered.Count == 0)
            {
                summary = $"Page 1 of 1  ·  Showing 0 of {allEntries.Count}";
            }
            else
            {
                int start = page * PageSize + 1;
                int end = Math.Min(filtered.Count, (page + 1) * PageSize);
                summary = filtered.Count == allEntries.Count
                    ? $"Page {page + 1} of {pages}  ·  Showing {start}-{end} of {filtered.Count}"
                    : $"Page {page + 1} of {pages}  ·  Showing {start}-{end} of {filtered.Count} (from {allEntries.Count})";
            }
            CoachLook.Write(bodyLayer, 370, 858, 1180, 34, summary,
                20, CoachLook.Soft, true, TextAlignmentOptions.Center);

            UiButton(bodyLayer, "NextPage", 1574, 852, 190, 46, "NEXT >",
                canNext ? CoachLook.Plan : CoachLook.Card,
                canNext ? CoachLook.Outline : CoachLook.CardLine,
                canNext ? CoachLook.Text : CoachLook.Dim,
                () => { if (page + 1 < PageCount()) { page++; DrawBody(); } }, 18, true, canNext);
        }

        // ---- Row Actions ----

        static void ConfirmDelete(ReplayEntry e)
        {
            CloseFilterPopup();
            popupLayer = CoachLook.At(pageRoot, "DeleteReplayDialog", 0, 0, CoachLook.W, CoachLook.H);
            CoachLook.Hot(popupLayer, 0, 0, CoachLook.W, CoachLook.H, CloseFilterPopup);
            CoachLook.Box(popupLayer, 540, 340, 840, 288, CoachLook.Panel, 8, clicks: true);
            CoachLook.Frame(popupLayer, 540, 340, 840, 288, CoachLook.PanelLine, 2);
            CoachLook.Write(popupLayer, 580, 362, 760, 46, "Delete replay from this PC?", 29, CoachLook.Text, true);
            var name = CoachLook.Write(popupLayer, 580, 420, 760, 38, e.fileName, 20, CoachLook.Text);
            name.richText = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            CoachLook.Write(popupLayer, 580, 467, 760, 62,
                $"Permanently delete {e.localCopies.Count} local log file(s)? Shared links will still work.", 19, CoachLook.Soft);
            UiButton(popupLayer, "CancelDelete", 880, 548, 210, 52, "Cancel", CoachLook.Card,
                CoachLook.Outline, CoachLook.Text, CloseFilterPopup);
            UiButton(popupLayer, "ConfirmDelete", 1110, 548, 230, 52, "Delete from PC", BadgeLoss,
                CoachLook.Outline, Color.white, () => DeleteEntry(e));
        }

        static void DeleteEntry(ReplayEntry e)
        {
            CloseFilterPopup();
            int failed = 0;
            foreach (var file in e.localCopies)
            {
                try
                {
                    var current = new FileInfo(file.FullName);
                    if (!current.Exists) continue;
                    if (current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteTimeUtc)
                        throw new IOException("Log changed since the library was opened.");
                    File.Delete(current.FullName);
                }
                catch (Exception ex)
                {
                    failed++;
                    Plugin.Log.LogWarning("Delete replay: " + file.FullName + ": " + ex.Message);
                }
            }
            ScanEntries();
            UpdateHeaderStats();
            SyncFilterButton();
            DrawBody();
            SetStatus(failed == 0 ? "Replay deleted from this PC." :
                $"Could not delete {failed} local log file(s). Refresh the library and retry.", failed != 0);
        }

        static void OpenEntry(ReplayEntry e)
        {
            if (e == null) return;
            CloseFilterPopup();
            try
            {
                if (ReplayRoom.Active) ReplayRoom.Exit();
                string result = ReplayRoom.Open(e.path);
                if (ReplayRoom.Active) Close();
                else SetStatus(string.IsNullOrEmpty(result) ? "Could not open this replay." : result, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ReplayLibrary.OpenEntry failed: " + ex);
                SetStatus("Could not open this replay.", true);
            }
        }

        static void ShareEntry(ReplayEntry e)
        {
            if (e == null) return;
            CloseFilterPopup();
            int token = ++opToken;
            SetStatus("Creating public replay link...", false);
            try
            {
                Account.ShareReplay(e.path, (ok, url, msg) =>
                {
                    if (token != opToken) return;
                    if (ok)
                    {
                        e.isShared = true;
                        SetStatus(string.IsNullOrEmpty(url) ? (msg ?? "Link copied!") : "Link copied! " + url, false);
                        if (Visible) DrawBody();
                    }
                    else
                    {
                        SetStatus(string.IsNullOrEmpty(msg) ? "Could not share replay." : msg, true);
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("ReplayLibrary.ShareEntry failed: " + ex);
                SetStatus("Could not share replay: " + ex.Message, true);
            }
        }

        // ---- Scanning & Caching ----

        static void ScanEntries()
        {
            allEntries.Clear();
            distinctLeaders.Clear();

            string sharedDir = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "SharedReplays"));
            string notesDir = Path.Combine(Application.persistentDataPath, "ReplayNotes");
            var candidateDirs = new List<(string path, bool isShared)>
            {
                (SafeFullPath(Path.Combine(Application.dataPath, "..", "..", "partite")), false),
                (SafeFullPath("partite"), false),
                (SafeFullPath(Path.Combine(Application.dataPath, "..", "CombatLogs", "AutoSaved")), false),
                (SafeFullPath(Path.Combine("CombatLogs", "AutoSaved")), false),
                (SafeFullPath(Store.Logs), false),
                (sharedDir, true)
            };

            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dedup = new Dictionary<string, (FileInfo file, bool partial, bool fromShared)>(StringComparer.OrdinalIgnoreCase);

            var copies = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (dirPath, isShared) in candidateDirs)
            {
                if (string.IsNullOrEmpty(dirPath) || !seenDirs.Add(dirPath)) continue;
                try
                {
                    var dir = new DirectoryInfo(dirPath);
                    if (!dir.Exists) continue;
                    foreach (var f in dir.GetFiles("*.log"))
                    {
                        bool partial = f.Name.EndsWith(".partial.log", StringComparison.OrdinalIgnoreCase);
                        // Imported logs can share a filename without being the same game.
                        string key = GameLog.ParseName(f.Name, out var parsedId, out _) ? parsedId
                            : f.Name + "|" + (Account.FileHash(f.FullName) ?? f.FullName);
                        if (!copies.TryGetValue(key, out var files)) copies[key] = files = new List<FileInfo>();
                        files.Add(f);
                        if (dedup.TryGetValue(key, out var cur))
                        {
                            bool sharedFlag = cur.fromShared || isShared;
                            bool preferNew = (cur.partial && !partial)
                                || (cur.partial == partial && f.LastWriteTimeUtc > cur.file.LastWriteTimeUtc);
                            dedup[key] = preferNew ? (f, partial, sharedFlag) : (cur.file, cur.partial, sharedFlag);
                        }
                        else
                        {
                            dedup[key] = (f, partial, isShared);
                        }
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("ReplayLibrary scan error in " + dirPath + ": " + e.Message); }
            }

            // Build O(1) lookup for MatchFile records once per refresh
            var matchById = new Dictionary<string, MatchFile>(StringComparer.OrdinalIgnoreCase);
            var matchByLog = new Dictionary<string, MatchFile>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var m in Store.AllMatches())
                {
                    if (!string.IsNullOrEmpty(m.id)) matchById[m.id] = m;
                    if (!string.IsNullOrEmpty(m.logName)) matchByLog[m.logName] = m;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("ReplayLibrary Store.AllMatches error: " + e.Message); }

            using var sha = SHA256.Create();
            var leaderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in dedup)
            {
                var file = kv.Value.file;
                try
                {
                    byte[] bytes = File.ReadAllBytes(file.FullName);
                    string hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                    string text = Encoding.UTF8.GetString(bytes);
                    var lines = text.Replace("\r\n", "\n").Split('\n');

                    var summary = GameLog.Summarize(file.Name, lines);
                    var parsed = ReplayLog.Parse(text);

                    bool hasId = GameLog.ParseName(file.Name, out var parsedId, out _);
                    MatchFile match = null;
                    if (hasId && !string.IsNullOrEmpty(parsedId)) matchById.TryGetValue(parsedId, out match);
                    if (match == null) matchByLog.TryGetValue(file.Name, out match);

                    string myLeader = !string.IsNullOrEmpty(summary.myLeader) ? summary.myLeader : parsed.Leaders[1];
                    string oppLeader = !string.IsNullOrEmpty(summary.oppLeader) ? summary.oppLeader : parsed.Leaders[2];
                    string myName = parsed.Names[1];
                    string oppName = parsed.Names[2];
                    bool isSolo = summary.noNames || (string.IsNullOrEmpty(myName) && string.IsNullOrEmpty(oppName));

                    string result = summary.result;
                    if (result != "w" && result != "l")
                        result = parsed.Winner == 1 ? "w" : parsed.Winner == 2 ? "l" : "o";

                    int turns = summary.turns;
                    if (turns <= 0 && parsed.Beats.Count > 0) turns = parsed.Beats[parsed.Beats.Count - 1].turn;

                    string kind = kv.Value.fromShared ? "WEB LINK"
                        : match != null && !string.IsNullOrEmpty(match.matchKind) ? match.matchKind.ToUpperInvariant()
                        : !string.IsNullOrEmpty(summary.kind) ? summary.kind.ToUpperInvariant()
                        : summary.ladderMatch > 0 ? "LADDER"
                        : isSolo ? "SOLO"
                        : null;
                    string format = match != null && !string.IsNullOrEmpty(match.matchFormat) ? match.matchFormat : summary.format;

                    bool hasNotes = match != null && match.notes != null && match.notes.Count > 0;
                    if (!hasNotes)
                    {
                        string notesFile = Path.Combine(notesDir, hash + ".json");
                        if (File.Exists(notesFile))
                        {
                            try
                            {
                                string nt = File.ReadAllText(notesFile).Trim();
                                hasNotes = nt.Length > 2 && nt != "{}";
                            }
                            catch (IOException) { }
                        }
                    }

                    bool isShared = kv.Value.fromShared
                        || (match != null && !string.IsNullOrEmpty(match.replayId))
                        || Account.HasSharedUrl(file.FullName, out _);

                    var entryItem = new ReplayEntry
                    {
                        localCopies = copies[kv.Key],
                        path = file.FullName,
                        fileName = file.Name,
                        id = parsedId,
                        sha256 = hash,
                        date = GameDate(file),
                        hasTime = hasId,
                        fromSharedDir = kv.Value.fromShared,
                        isSolo = isSolo,
                        hasNotes = hasNotes,
                        isShared = isShared,
                        result = result,
                        turns = turns,
                        first = summary.first,
                        myLeader = myLeader,
                        oppLeader = oppLeader,
                        myLeaderDisplay = string.IsNullOrEmpty(myLeader) ? (summary.myLeaderName ?? "Unknown") : CoachUi.LeaderName(myLeader, summary.myLeaderName),
                        oppLeaderDisplay = string.IsNullOrEmpty(oppLeader) ? (summary.oppLeaderName ?? "Unknown") : CoachUi.LeaderName(oppLeader, summary.oppLeaderName),
                        myName = myName,
                        oppName = oppName,
                        kind = kind,
                        format = format
                    };
                    allEntries.Add(entryItem);

                    if (!string.IsNullOrEmpty(myLeader))
                        leaderCounts[myLeader] = leaderCounts.TryGetValue(myLeader, out var c) ? c + 1 : 1;
                }
                catch (Exception e) { Plugin.Log.LogWarning("ReplayLibrary skip " + file.Name + ": " + e.Message); }
            }

            allEntries.Sort((a, b) =>
            {
                int cmp = b.date.CompareTo(a.date);
                return cmp != 0 ? cmp : string.Compare(a.fileName, b.fileName, StringComparison.OrdinalIgnoreCase);
            });

            distinctLeaders.AddRange(leaderCounts.OrderByDescending(k => k.Value)
                .ThenBy(k => CoachUi.LeaderName(k.Key, null), StringComparer.OrdinalIgnoreCase)
                .Select(k => k.Key));

            if (leaderFilter != null && !distinctLeaders.Any(l => string.Equals(l, leaderFilter, StringComparison.OrdinalIgnoreCase)))
                leaderFilter = null;
        }

        static void ApplyFilters()
        {
            filtered.Clear();
            foreach (var e in allEntries)
            {
                if (activeFilter == FilterKind.Wins && e.result != "w") continue;
                if (activeFilter == FilterKind.Losses && e.result != "l") continue;
                if (activeFilter == FilterKind.Notes && !e.hasNotes) continue;
                if (activeFilter == FilterKind.Shared && !e.isShared) continue;
                if (leaderFilter != null && !string.Equals(e.myLeader, leaderFilter, StringComparison.OrdinalIgnoreCase)) continue;
                filtered.Add(e);
            }
            int pages = Math.Max(1, PageCount());
            if (page >= pages) page = pages - 1;
            if (page < 0) page = 0;
        }

        static int PageCount() => (filtered.Count + PageSize - 1) / PageSize;

        static string SafeFullPath(string p)
        {
            try { return string.IsNullOrEmpty(p) ? null : Path.GetFullPath(p); }
            catch (Exception) { return null; }
        }

        static DateTime GameDate(FileInfo file)
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (GameLog.ParseName(file.Name, out _, out var started)) return started.ToLocalTime();
            if (name.Length >= 10 && DateTime.TryParseExact(name.Substring(name.Length - 10), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
            return file.LastWriteTime;
        }

        // ---- Formatting & UI helpers ----

        static string FormatLeaderRich(string display, string code)
        {
            string namePart = CoachUi.Safe(Short(string.IsNullOrEmpty(display) ? (code ?? "Unknown") : display, 22));
            if (string.IsNullOrEmpty(code)) return namePart;
            return $"{namePart}  <size=16><color=#9aa6c2>{CoachUi.Safe(code)}</color></size>";
        }

        static string BuildModeLabel(string kind, string format)
        {
            if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(format))
                return Short(kind + " · " + format.ToUpperInvariant(), 20);
            if (!string.IsNullOrEmpty(kind)) return Short(kind, 16);
            if (!string.IsNullOrEmpty(format)) return Short(format.ToUpperInvariant(), 16);
            return null;
        }

        static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)).TrimEnd() + "…";
        }

        static GameObject UiButton(Transform parent, string name, float x, float y, float w, float h,
            string label, Color fill, Color border, Color textColor, Action click,
            float fontSize = 16, bool bold = true, bool interactable = true,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var img = CoachLook.Box(parent, x, y, w, h, fill, 4, clicks: true);
            img.gameObject.name = name;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = border;
            outline.effectDistance = new Vector2(1, -1);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = interactable;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.18f, 1f);
            colors.pressedColor = new Color(0.86f, 0.88f, 0.94f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.70f, 0.72f, 0.78f, 0.55f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (click != null) btn.onClick.AddListener(() => { ClearSelection(); Safe(click); });

            float padX = align == TextAlignmentOptions.Center ? 8 : 12;
            var t = CoachLook.Write(img.transform, padX, 0, w - padX * 2, h, label, fontSize, textColor, bold, align);
            t.richText = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return img.gameObject;
        }

        static void ClearSelection()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        static void Safe(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Plugin.Log.LogWarning("ReplayLibrary: " + e.Message); }
        }
    }
}
