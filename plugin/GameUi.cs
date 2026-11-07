// Quello che la mod aggiunge alla schermata di gioco:
//  - due pulsanti blu nella colonna di destra, sotto "Restart this turn": Add note e Focus mode;
//  - il pannello per scrivere una nota, sopra il log (la nota si ancora al momento in cui l'hai aperto);
//  - Focus mode: nasconde tutto tranne il tavolo, le scelte, Back to Main, Concede e i popup dell'avversario;
//  - a fine partita, sotto YOU WIN / YOU LOSE, il pulsante grande per aprire la partita su OPTCG Trainer.
// I pulsanti sono copie di quelli di Batsu (Look.CloneButton). Le cose si nascondono con una CanvasGroup
// (trasparenti e non cliccabili), mai spegnendole: il codice del sim le trova e le usa come sempre.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    static class GameUi
    {
        // Focus mode: i GameObject di GameplayLogicScript da nascondere (il resto resta: tavolo, scelte, Back to Main,
        // Concede, popup "l'avversario chiede di annullare / ricominciare il turno", nomi e tempi dei giocatori)
        static readonly string[] FocusHides =
        {
            "go_LogView", "go_ChatBox", "go_ChatButton", "go_ChatMute", "go_TwitchButton",
            "go_ReportButton", "go_ReportYes", "go_ReportNo", "go_ReportFeedback",
            "go_DownloadLog", "go_ReportBug", "go_SaveGameState", "go_RequestCancelBtn",
            "go_GameStateRestartTurn", "go_GameStateBackTurn", "go_GameStateForwardTurn",
            "go_MusicToggle", "go_SoundToggle",
        };

        static GameplayLogicScript game;
        static float nextFind;
        static GameObject noteButton, focusButton, endBox, endButton, panel;
        static TextMeshProUGUI endStatus, panelTitle, panelFooter;
        static TMP_InputField input;
        static int noteLine, noteTurn, generation = -1;
        static bool openWhenReady, focusApplied, wasInGame;
        static float savedFlashUntil;
        static readonly Dictionary<GameObject, (float alpha, bool blocks, bool interactable, bool added)> hidden =
            new Dictionary<GameObject, (float, bool, bool, bool)>();

        /// Si sta scrivendo una nota: il sim non deve leggere i tasti (H ordina la mano, T il trash).
        internal static bool Typing => panel != null && panel.activeInHierarchy && input != null && input.isFocused;

        internal static void Tick()
        {
            // nel menu principale la finestra torna normale (Focus mode resta ricordato: alla prossima partita torna a schermo intero)
            if (Plugin.WindowBeforeFocus.Value != "" && MenuButton.Instance != null && MenuButton.Instance.activeInHierarchy) Fullscreen(false);
            if (game == null)
            {
                if (Time.realtimeSinceStartup < nextFind) return;
                nextFind = Time.realtimeSinceStartup + 1f;
                game = UnityEngine.Object.FindAnyObjectByType<GameplayLogicScript>();
                noteButton = focusButton = endBox = endButton = panel = null;
                hidden.Clear();
                focusApplied = false;
                if (game == null) return;
            }

            bool inGame = game.go_ReturnToMainMenu != null && game.go_ReturnToMainMenu.activeInHierarchy;
            Match.Track(game);
            if (inGame) Knowledge.Track(game);
            if (inGame && noteButton == null) Build();
            if (noteButton == null) return;

            if (generation != Match.Generation)
            {
                // partita nuova: via il pannello di fine partita e la nota a metà
                generation = Match.Generation;
                openWhenReady = false;
                ClosePanel();
            }

            // durante lo scatto della foto da condividere i pulsanti della mod non si vedono (un fotogramma)
            bool show = inGame && !ShareShot.Hiding;
            noteButton.SetActive(show);
            focusButton.SetActive(show);
            if (ShareShot.Button != null) ShareShot.Button.SetActive(show);
            ShareShot.Tick();
            ApplyFocus(inGame && Plugin.FocusMode.Value);
            // si entra in partita con Focus mode acceso: schermo intero (se non lo è già)
            if (inGame != wasInGame)
            {
                wasInGame = inGame;
                if (inGame && Plugin.FocusMode.Value) Fullscreen(true);
            }

            var match = Match.ForGame(game);
            bool finished = match != null && match.Finished;
            bool showEnd = show && finished;
            if (showEnd && !endBox.activeSelf) PlaceEnd(); // ogni volta che compare: la finestra può aver cambiato misura
            endBox.SetActive(showEnd);
            if (inGame && finished) UpdateEnd(match);

            int count = match != null ? match.notes.Count : 0;
            Look.SetLabel(noteButton, Time.realtimeSinceStartup < savedFlashUntil ? "Note saved" : count > 0 ? $"Add note ({count})" : "Add note");
            var b = noteButton.GetComponentInChildren<Button>(true);
            if (b != null) b.interactable = match != null;
            Look.SetLabel(focusButton, Plugin.FocusMode.Value ? "Exit focus mode" : "Focus mode");
            Look.SetTint(focusButton, Plugin.FocusMode.Value ? Look.GoldTint : Look.Blue);

            if (panel != null && panel.activeSelf)
            {
                if (!inGame || Input.GetKeyDown(KeyCode.Escape)) ClosePanel(); // con il riquadro della nota aperto, Esc chiude solo lui
                else if (ShareShot.Hiding) panel.SetActive(false); // lo scatto lo chiude: non deve finire nella foto
            }
            else if (inGame && Plugin.FocusMode.Value && Input.GetKeyDown(KeyCode.Escape)) ToggleFocus(); // Esc esce da Focus mode
        }

        // ---- costruzione ----

        static void Build()
        {
            try
            {
                var download = game.go_DownloadLog;
                var step = game.go_ReportBug.transform.position - download.transform.position; // un pulsante più giù
                var cancel = game.go_RequestCancelBtn.transform.position;

                noteButton = Look.CloneButton(download, "OPTCGTrainerNote", "Add note", Look.Blue, OpenPanel);
                noteButton.transform.position = cancel + step * 2.3f;
                focusButton = Look.CloneButton(download, "OPTCGTrainerFocus", "Focus mode", Look.Blue, ToggleFocus);
                focusButton.transform.position = cancel + step * 3.3f;
                foreach (var go in new[] { noteButton, focusButton }) NoLayout(go);

                BuildEnd(download);
                BuildPanel(download);
                ShareShot.Build(game, download);
                Plugin.Log.LogInfo("pulsanti della partita aggiunti");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("pulsanti della partita non aggiunti: " + e);
                foreach (var go in new[] { noteButton, focusButton, endBox, panel, ShareShot.Button }) if (go != null) UnityEngine.Object.Destroy(go);
                noteButton = null;
                game = null; // si riprova tra un secondo, senza bloccare il sim
            }
        }

        // se il genitore dispone i figli da solo (LayoutGroup), le nostre copie restano fuori dal suo conto
        static void NoLayout(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        static void BuildEnd(GameObject frameTemplate)
        {
            var choice = game.go_ChoiceButton1;
            var choiceRect = (RectTransform)choice.transform;
            float bw = choiceRect.rect.width, bh = choiceRect.rect.height;

            // il riquadro sotto YOU WIN / YOU LOSE: la cornice di Batsu in blu notte, sopra a tutto (anche alle carte)
            endBox = new GameObject("OPTCGTrainerEnd", typeof(RectTransform), typeof(Image));
            var box = (RectTransform)endBox.transform;
            box.SetParent(choice.transform.parent, false);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(bw * 1.3f, bh * 2.2f);
            PlaceEnd();
            NoLayout(endBox);
            Frame(endBox, frameTemplate);
            OnTop(endBox);

            // il pulsante grande: copia di una scelta (Keep / Mulligan), con il logo che esce dal bordo
            endButton = Look.CloneButton(choice, "OPTCGTrainerOpen", "", Look.Blue, EndClick);
            endButton.transform.SetParent(box, false);
            Look.Place((RectTransform)endButton.transform, 0.15f, 0.08f, 0.79f, 0.5f);
            Look.AddLogoOut(endButton, 1.5f);

            var label = endButton.GetComponentInChildren<TMP_Text>(true);
            endStatus = Look.NewText(box, label, "Status", bh * 0.22f, TextAlignmentOptions.Center);
            Look.Place(endStatus.rectTransform, 0.06f, 0.63f, 0.88f, 0.31f);
            endStatus.enableAutoSizing = true;
            endStatus.fontSizeMin = 8;
            endStatus.fontSizeMax = bh * 0.24f;
            endBox.SetActive(false);
        }

        // sotto YOU WIN / YOU LOSE, al centro
        static void PlaceEnd()
        {
            var win = (RectTransform)game.go_YouWin.transform;
            var box = (RectTransform)endBox.transform;
            box.position = Center(win) - Vector3.up * (WorldHeight(win) * 0.5f + WorldHeight(box) * 0.5f);
        }

        // lo sfondo dei nostri riquadri: la cornice dei pulsanti di Batsu, tinta di blu notte
        static void Frame(GameObject go, GameObject template)
        {
            var bg = go.GetComponent<Image>();
            var frame = template.GetComponentInChildren<Image>(true);
            if (frame != null) { bg.sprite = frame.sprite; bg.type = Image.Type.Sliced; }
            bg.color = Look.PanelTint;
        }

        // un canvas tutto suo, nell'ultimo strato e con l'ordine più alto: niente (carte comprese) ci passa sopra
        static void OnTop(GameObject go)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            var layers = SortingLayer.layers;
            if (layers.Length > 0) canvas.sortingLayerID = layers[layers.Length - 1].id;
            canvas.sortingOrder = 30000;
            go.AddComponent<GraphicRaycaster>();
        }

        static void BuildPanel(GameObject buttonTemplate)
        {
            var log = (RectTransform)game.go_LogView.transform;
            panel = new GameObject("OPTCGTrainerNotePanel", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(log.parent, false);
            prt.anchorMin = log.anchorMin; prt.anchorMax = log.anchorMax; prt.pivot = log.pivot;
            prt.anchoredPosition = log.anchoredPosition; prt.sizeDelta = log.sizeDelta; prt.localScale = log.localScale;
            prt.SetAsLastSibling();
            NoLayout(panel);
            Frame(panel, buttonTemplate);
            OnTop(panel);

            var font = buttonTemplate.GetComponentInChildren<TMP_Text>(true);
            float unit = prt.rect.height;
            panelTitle = Look.NewText(prt, font, "Title", unit * 0.075f, TextAlignmentOptions.Left);
            Look.Place(panelTitle.rectTransform, 0.07f, 0.04f, 0.86f, 0.11f);
            panelTitle.fontStyle = FontStyles.Bold;

            // il campo di testo: copia di quello della chat del sim, senza i suoi collegamenti
            var field = Look.SafeCopy(game.go_ChatBox, prt);
            field.name = "Text";
            input = field.GetComponent<TMP_InputField>();
            if (input == null) throw new InvalidOperationException("la chat del sim non è un TMP_InputField");
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.lineType = TMP_InputField.LineType.MultiLineSubmit; // va a capo da solo, Invio salva
            input.characterLimit = 1000;
            input.onSubmit.AddListener(_ => SaveNote());
            Look.Place((RectTransform)field.transform, 0.07f, 0.17f, 0.86f, 0.5f);
            if (input.textComponent != null)
            {
                input.textComponent.fontSize = unit * 0.06f;
                input.textComponent.textWrappingMode = TextWrappingModes.Normal;
                input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            }
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "What happened here? e.g. maybe I shouldn't have played this card";
                placeholder.fontSize = unit * 0.055f;
                placeholder.textWrappingMode = TextWrappingModes.Normal;
                placeholder.alignment = TextAlignmentOptions.TopLeft;
            }

            var save = Look.CloneButton(buttonTemplate, "Save", "Save", Look.Blue, SaveNote);
            save.transform.SetParent(prt, false);
            Look.Place((RectTransform)save.transform, 0.07f, 0.71f, 0.41f, 0.14f);
            var cancel = Look.CloneButton(buttonTemplate, "Cancel", "Cancel", Color.white, ClosePanel);
            cancel.transform.SetParent(prt, false);
            Look.Place((RectTransform)cancel.transform, 0.52f, 0.71f, 0.41f, 0.14f);
            var cancelText = cancel.GetComponentInChildren<TMP_Text>(true);
            if (cancelText != null) { cancelText.fontMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.white); cancelText.color = new Color(0.1f, 0.1f, 0.12f); }

            panelFooter = Look.NewText(prt, font, "Footer", unit * 0.045f, TextAlignmentOptions.Left);
            Look.Place(panelFooter.rectTransform, 0.07f, 0.87f, 0.86f, 0.1f);
            panelFooter.color = new Color(1, 1, 1, 0.8f);
            panel.SetActive(false);
        }

        static float WorldHeight(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return c[1].y - c[0].y;
        }

        static Vector3 Center(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return (c[0] + c[2]) / 2;
        }

        // ---- note ----

        static void OpenPanel()
        {
            if (Match.ForGame(game) == null) return;
            (noteLine, noteTurn) = Match.Now(game); // il momento della nota è adesso, anche se intanto la partita va avanti
            panelTitle.text = noteTurn > 0 ? $"Note · Turn {noteTurn}" : "Note";
            int count = Match.ForGame(game).notes.Count;
            panelFooter.text = count == 0 ? "It goes into the replay, at this moment." : $"{count} so far. They go into the replay.";
            input.text = "";
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            input.Select();
            input.ActivateInputField();
        }

        static void SaveNote()
        {
            if (panel == null || !panel.activeSelf) return;
            var text = input.text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Match.AddNote(game, noteLine, noteTurn, text);
                savedFlashUntil = Time.realtimeSinceStartup + 1.6f;
            }
            ClosePanel();
        }

        static void ClosePanel()
        {
            if (panel == null) return;
            if (input != null) input.DeactivateInputField();
            panel.SetActive(false);
        }

        // ---- Focus mode ----

        static void ToggleFocus()
        {
            Plugin.FocusMode.Value = !Plugin.FocusMode.Value; // BepInEx lo salva nel suo file di impostazioni
            Plugin.Log.LogInfo("Focus mode " + (Plugin.FocusMode.Value ? "acceso" : "spento"));
            Fullscreen(Plugin.FocusMode.Value);
        }

        // Focus mode a schermo intero, come F11 nel browser: una finestra senza bordi grande quanto lo schermo dove sta il sim.
        // Niente permessi di Windows: è la stessa cosa che fa l'opzione a schermo intero del sim. Spegnendo, torna la finestra
        // di prima (ricordata nelle impostazioni, così vale anche se intanto il sim è stato chiuso e riaperto).
        internal static void Fullscreen(bool on)
        {
            if (on)
            {
                if (!Plugin.FocusFullscreen.Value) return;
                if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow || Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen) return; // lo è già
                Plugin.WindowBeforeFocus.Value = $"{(int)Screen.fullScreenMode},{Screen.width},{Screen.height}";
                var display = Screen.mainWindowDisplayInfo;
                Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
                Plugin.Log.LogInfo($"schermo intero {display.width}x{display.height} (prima: {Plugin.WindowBeforeFocus.Value})");
            }
            else
            {
                var was = Plugin.WindowBeforeFocus.Value.Split(',');
                if (was.Length != 3 || !int.TryParse(was[0], out var mode) || !int.TryParse(was[1], out var w) || !int.TryParse(was[2], out var h)) return;
                Plugin.WindowBeforeFocus.Value = "";
                Screen.SetResolution(w, h, (FullScreenMode)mode);
                Plugin.Log.LogInfo($"di nuovo in finestra {w}x{h}");
            }
        }

        static void ApplyFocus(bool on)
        {
            if (on == focusApplied && (!on || Time.frameCount % 30 != 0)) return; // ogni mezzo secondo circa si ricontrolla
            focusApplied = on;
            // la chat nascosta non deve restare attiva: un Invio manderebbe un messaggio all'avversario senza vederlo
            var chat = on && game.go_ChatBox != null ? game.go_ChatBox.GetComponent<TMP_InputField>() : null;
            if (chat != null && chat.isFocused) chat.DeactivateInputField();
            foreach (var name in FocusHides)
            {
                var go = AccessTools.Field(typeof(GameplayLogicScript), name)?.GetValue(game) as GameObject;
                if (go != null) SetHidden(go, on);
            }
            // il pulsante "Open OPBounty" in alto a destra (non è del GameplayLogicScript)
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b.gameObject.name != "MinmizeButton") continue;
                // solo "Open OPBounty": col pannello aperto la scritta diventa "Hide OPBounty" e il pulsante serve per chiuderlo
                var t = b.GetComponentInChildren<TMP_Text>(true);
                if (t != null && t.text.TrimStart().StartsWith("Open") && t.text.Contains("OPBounty")) SetHidden(b.gameObject, on);
                else if (hidden.ContainsKey(b.gameObject)) SetHidden(b.gameObject, false);
            }
        }

        static void SetHidden(GameObject go, bool hide)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (hide)
            {
                if (!hidden.ContainsKey(go))
                {
                    bool added = group == null;
                    if (added) group = go.AddComponent<CanvasGroup>();
                    hidden[go] = (group.alpha, group.blocksRaycasts, group.interactable, added);
                }
                group.alpha = 0;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            else if (hidden.TryGetValue(go, out var was))
            {
                hidden.Remove(go);
                if (group == null) return;
                if (was.added) UnityEngine.Object.Destroy(group);
                else { group.alpha = was.alpha; group.blocksRaycasts = was.blocks; group.interactable = was.interactable; }
            }
        }

        // ---- fine partita ----

        static void UpdateEnd(MatchFile m)
        {
            string top, status;
            if (!Account.Linked)
            {
                if (Account.Linking)
                {
                    top = "CONFIRM ON THE WEBSITE";
                    status = $"Your browser opened OPTCG Trainer. Check the code is <b>{Account.LinkCode}</b> and press Link.";
                }
                else
                {
                    top = "LINK YOUR ACCOUNT";
                    status = Account.LastError ?? "Link once: from then on every game goes to your account by itself.";
                }
            }
            else if (m.uploadError != null)
            {
                top = "OPEN";
                status = "This game wasn't uploaded: " + m.uploadError + ".";
            }
            else if (m.replayId == null)
            {
                top = "UPLOADING TO";
                status = Account.LastError ?? $"Saving this game to {Safe(Account.Nickname)}'s account…";
            }
            else
            {
                top = "OPEN THIS GAME IN";
                int n = m.notes.Count;
                status = $"Saved on {Safe(Account.Nickname)}'s account" + (n > 0 ? $" · {n} note{(n == 1 ? "" : "s")}" : "") + (m.PendingNotes > 0 ? " (uploading notes…)" : "");
            }
            Look.SetLabel(endButton, $"<size=62%>{top}</size>\nOPTCG <color={Look.GoldHex}>TRAINER</color>");
            if (endStatus.text != status) endStatus.text = status;

            if (openWhenReady && m.replayId != null && m.PendingNotes == 0)
            {
                openWhenReady = false;
                Account.OpenReplay(m.replayId);
            }
        }

        // un testo che arriva dal sito (il nickname) si mostra così com'è, senza che diventi formattazione
        static string Safe(string text) => "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";

        static void EndClick()
        {
            var m = Match.ForGame(game);
            if (m == null) return;
            if (!Account.Linked) { openWhenReady = true; Account.StartLink(); return; }
            if (m.uploadError != null || (m.replayId != null && m.site != Account.Site)) { Account.Open(Account.Site); return; }
            if (m.replayId != null && m.PendingNotes == 0) { Account.OpenReplay(m.replayId); return; }
            openWhenReady = true; // si apre appena il caricamento finisce
            Account.Kick();
        }

        // ---- i clic e i tasti non devono passare al tavolo ----

        internal static bool PointerOverMod() => Over(Input.mousePosition);

        /// true se il punto dello schermo (in pixel, dal basso a sinistra come Input.mousePosition) è sopra la mod.
        internal static bool Over(Vector2 p)
        {
            if (noteButton == null) return false;
            // gira a ogni fotogramma: solo i quattro riquadri, che contengono già tutto il resto (il logo sta dentro endBox)
            return Look.Contains(noteButton.transform as RectTransform, p) || Look.Contains(focusButton.transform as RectTransform, p)
                || (endBox != null && Look.Contains(endBox.transform as RectTransform, p)) || (panel != null && Look.Contains(panel.transform as RectTransform, p))
                || (ShareShot.Button != null && Look.Contains(ShareShot.Button.transform as RectTransform, p));
        }

        internal static bool AnyMouseDown => Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
    }

    // Il sim legge H (ordina la mano) e T (ordina il trash) se la sua chat non è attiva: anche mentre scrivi una nota no.
    [HarmonyPatch(typeof(GameplayLogicScript), "HandleKeyboardAction")]
    static class NoKeysWhileTyping
    {
        static bool Prefix()
        {
            try { return !GameUi.Typing; }
            catch (Exception) { return true; } // un errore della mod non deve mai fermare il sim
        }
    }

    // Il sim guarda da solo cosa c'è sotto il mouse (le carte), anche sotto i pannelli: sopra i nostri non deve.
    [HarmonyPatch(typeof(GameplayLogicScript), "HandleMouseHover")]
    static class NoHoverOverMod
    {
        static bool Prefix(GameplayLogicScript __instance)
        {
            try
            {
                if (!GameUi.PointerOverMod()) return true;
                __instance.go_FocusedObject = null; // così un clic che finisce qui non colpisce l'ultima carta passata col mouse
                return false;
            }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "HandleMouseClick")]
    static class NoClickOverMod
    {
        // solo i clic che cominciano sopra la mod: un trascinamento di una carta finisce sempre normalmente
        static bool Prefix()
        {
            try { return !(GameUi.AnyMouseDown && GameUi.PointerOverMod()); }
            catch (Exception) { return true; }
        }
    }

    [HarmonyPatch(typeof(GameplayLogicScript), "HandleRightMouseClick")]
    static class NoRightClickOverMod
    {
        static bool Prefix()
        {
            try { return !(GameUi.AnyMouseDown && GameUi.PointerOverMod()); }
            catch (Exception) { return true; }
        }
    }
}
