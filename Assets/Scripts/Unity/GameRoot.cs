using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    public sealed partial class GameRoot : MonoBehaviour
    {
        private GameUi ui;
        private GameFlow flow;
        private BitmapFontRegistry fonts;
        private GameData data => flow.Data;
        private SaveData save => flow.Save;
        private RectTransform page, overlay, splashLogo;
        private CanvasGroup dialogTapHint;
        private string savePath;
        private string musicTrack = "music_map_loop";

        private void Awake()
        {
            Application.targetFrameRate = 60;
            ui = new GameUi(gameObject);
            try
            {
                var snapshot = Resources.Load<TextAsset>("Dynamons/gameplay");
                if (snapshot == null) throw new InvalidOperationException("Gameplay snapshot not found. Run unity/tools/import-web.mjs.");
                var gameData = JsonUtility.FromJson<GameData>(snapshot.text);
                if (gameData == null || gameData.mons == null || gameData.mapNodes == null) throw new InvalidOperationException("Invalid gameplay snapshot.");
                gameData.Normalize();
                savePath = Path.Combine(Application.persistentDataPath, "Dynamons2", "save.json");
                var loaded = SaveStore.Load(savePath, gameData);
                var random = new System.Random();
                flow = new GameFlow(gameData, loaded, random.NextDouble);
                fonts = new BitmapFontRegistry(gameData, ui);
                if (save.Event.Sync(DateTime.Today)) Persist();
                Splash();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                BeginPage(null);
                ui.Label(page, "COULD NOT LOAD GAME\n" + error.Message, 14, 148, 292, 220, 15);
            }
        }

        private DateTime checkedDay;
        private void Update()
        {
            ui?.Resize();
            if (splashLogo != null)
                splashLogo.anchoredPosition = new Vector2(34f, -(14f + Mathf.Sin(Time.unscaledTime * 3f) * 2.5f));
            if (dialogTapHint != null)
                dialogTapHint.alpha = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
            UpdateMapAnimations();
            UpdateBattleLoop();
            UpdateCollectionAnimations();
            if (flow == null) return;
            var day = DateTime.Today;
            if (day == checkedDay) return;
            checkedDay = day;
            if (save.Event.Sync(day)) { Persist(); RefreshTickets(); }
        }

        private bool Persist()
        {
            try { SaveStore.Save(savePath, save, data); return true; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { Debug.LogError(error); return false; }
        }

        private void PersistThen(Action done)
        {
            if (Persist()) { CloseOverlay(); done(); return; }
            var panel = OpenOverlay("SAVE FAILED", 172);
            ui.Label(panel, "The change is not saved. Free some storage or check permissions, then retry.", 12, 42, 272, 70, 14);
            ui.Button(panel, "RETRY SAVE", 83, 122, 130, 36, () => PersistThen(done));
        }

        private void BeginPage(string art = "map/bgs/bg1")
        {
            StopAllCoroutines();
            splashLogo = null;
            dialogTapHint = null;
            ResetMapVisualRefs();
            ResetBattleState();
            ResetCollectionState();
            starterModalDynamic = null;
            GameUi.Clear(ui.Frame);
            overlay = null;
            page = GameUi.Box("Screen", ui.Frame, 0f, 0f, GameUi.Width, GameUi.Height);
            GameUi.Fill(page, GameUi.Ink, 0f, 0f, GameUi.Width, GameUi.Height);
            if (art != null) ui.Art(page, art, 0f, 0f, GameUi.Width, GameUi.Height);
        }

        private void Track(string name) { musicTrack = name; ui.Music(name, save); }

        private void OpenOverlayShell(float dimAlpha = 0.72f)
        {
            CloseOverlay();
            var input = page.GetComponent<CanvasGroup>() ?? page.gameObject.AddComponent<CanvasGroup>();
            input.interactable = false; input.blocksRaycasts = false;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            overlay = GameUi.Box("Modal", ui.Frame, 0f, 0f, GameUi.Width, GameUi.Height);
            GameUi.Fill(overlay, new Color(0.035f, 0.02f, 0.078f, dimAlpha), 0f, 0f, GameUi.Width, GameUi.Height, true);
        }

        private RectTransform OpenOverlay(string title, int height)
        {
            OpenOverlayShell(0.65f);
            var panel = GameUi.Box(title, overlay, 12f, (GameUi.Height - height) * 0.5f, 296f, height);
            var vec = VectorCanvas.Create("ModalPanelBg", panel, 0f, 0f, 296f, height);
            vec.FillRoundRect(0f, 0f, 296f, height, 12f, GameUi.HexColor("#e8b35a"));
            vec.FillRoundRect(3f, 3f, 290f, height - 6f, 10f, GameUi.HexColor("#231a38"));
            vec.Commit();
            if (fonts != null) fonts.Draw(panel, "white_stroked_big", title, 148f, 14f, 16f, "center", null, 272f);
            else ui.Label(panel, title, 6, 8, 284, 28, 18, GameUi.Gold);
            return panel;
        }

        private void Note(string text)
        {
            var panel = OpenOverlay("NOTICE", 172);
            if (fonts != null)
            {
                fonts.DrawLines(panel, "white_stroked_small", text, 148f, 52f, 10f, 260f, 15f, "center");
                var btnVec = VectorCanvas.Create("OkBg", panel, 0f, 0f, 296f, 172f);
                btnVec.FillRoundRect(98f, 120f, 100f, 34f, 8f, GameUi.HexColor("#2b8a4e"));
                btnVec.Commit();
                fonts.Draw(panel, "white_stroked_small", "OK", 148f, 130f, 12f, "center");
                ui.HitBox(panel, 98f, 120f, 100f, 34f, () => { ui.Sound("button_click", save); CloseOverlay(); });
            }
            else
            {
                ui.Label(panel, text, 12, 42, 272, 70, 14);
                ui.Button(panel, "OK", 98, 122, 100, 36, CloseOverlay);
            }
        }

        private void CloseOverlay()
        {
            if (overlay == null) return;
            starterModalDynamic = null;
            overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); overlay = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if (page != null)
            {
                var input = page.GetComponent<CanvasGroup>();
                if (input != null) { input.interactable = true; input.blocksRaycasts = true; }
            }
        }

        public void DrawMapStrips(Transform parent, float offsetX, bool cullToScreen = true)
        {
            var widths = new[] { 395f, 320f, 320f, 395f };
            float tileX = offsetX - 75f;
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                if (!cullToScreen || (tileX + w >= 0f && tileX <= GameUi.Width))
                    ui.Art(parent, "map/bgs/bg" + (i + 1), tileX, 0f, w, GameUi.Height);
                tileX += w;
            }
        }

        private void Splash()
        {
            BeginPage(null); Track("music_map_loop");
            ui.Art(page, "splash/splash", 0f, -325f, 320f, 893f);
            splashLogo = ui.Art(page, "splash/logo", 34f, 14f, 252f, 100f, true).rectTransform;
            ui.ArtButton(page, "splash/btn_play", 102.5f, 336f, 115f, 48f, () =>
            {
                ui.Sound("button_click", save);
                if (save.Team.Count == 0) Choose(); else ShowMap();
            }, true, 1f, "PlayButton");
        }

        private void Choose()
        {
            BeginPage(null); Track("music_map_loop");
            ui.Art(page, "map/bgs/bg1", -75f, 0f, 395f, 568f);
            GameUi.Fill(page, new Color(0f, 0f, 0f, 0.70f), 0f, 0f, GameUi.Width, GameUi.Height);

            var choices = (data.tutorial.chooseMons ?? Array.Empty<Choice>()).Take(3).ToArray();
            float panelW = 100f, panelH = 210f;
            float groupW = choices.Length * panelW + Mathf.Max(0, choices.Length - 1) * 4f;
            float groupH = Mathf.Max(panelH, panelH * 0.97f + 22.5f);
            float groupX = GameUi.Width * 0.5f - groupW * 0.5f;
            float groupY = GameUi.Height * 0.5f - groupH * 0.5f - 30f;

            for (int i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                float x = groupX + i * 104f;
                float y = groupY + 30f;
                ui.Art(page, $"onboarding/choose_panel_{choice.type}", x, y, panelW, panelH);
                string title = (data.Mon(choice.id)?.title ?? choice.id).ToUpperInvariant();
                fonts.Draw(page, "white_stroked_big", title, x + 50f, y + 36f, 15f, "center", null, 88f);
                ui.Art(page, GameUi.Mon(choice.id, "front"), x + 5f, y + 60f, 90f, 98f, true);
                ui.ArtButton(page, "onboarding/btn_choose", x + 2.5f, y + panelH * 0.97f - 22.5f, 95f, 45f,
                    () => ChooseMon(choice.id), true, 1f, "Choose_" + choice.id);
            }
            ui.Art(page, "onboarding/choose_title", 50f, groupY, 220f, 20f);
        }

        private void ChooseMon(string id)
        {
            if (!flow.ChooseStarter(id)) return;
            if (!Persist())
            {
                save.Team.Clear();
                Note("Could not save. Please try again.");
                return;
            }
            ui.Sound("button_click", save);
            Dialogue("hello_world", ShowMap);
        }

        private void Dialogue(string id, Action done)
        {
            var dialog = data.Dialog(id);
            if (string.IsNullOrEmpty(id) || save.SeenDialogs.Contains(id) || (dialog?.content?.Length ?? 0) == 0)
            {
                done();
                return;
            }
            int index = 0;
            void RenderLine()
            {
                if (index >= dialog.content.Length)
                {
                    save.SeenDialogs.Add(id);
                    Persist();
                    done();
                    return;
                }
                BeginPage(null); Track("music_map_loop");
                var node = data.Node(save.NodeId);
                float focusX = node?.pos?.Length > 0 ? node.pos[0] : 160f;
                float offsetX = Mathf.Clamp(GameUi.Width * 0.5f - focusX, -1035f, 0f);
                DrawMapStrips(page, offsetX, true);
                GameUi.Fill(page, new Color(0f, 0f, 0f, 0.60f), 0f, 0f, GameUi.Width, GameUi.Height);

                var line = dialog.content[index++];
                string rawWho = string.IsNullOrEmpty(line.who) ? "left:jovani" : line.who;
                var parts = rawWho.Split(':');
                string side = parts.Length > 1 ? parts[0] : "left";
                string portraitKey = parts.Length > 1 ? parts[1] : parts[0];
                bool isLeft = side != "right";
                string speaker = portraitKey.Split('-')[0].ToUpperInvariant();

                ui.Art(page, $"general/dialog/{portraitKey}", isLeft ? 0f : GameUi.Width - 144f, GameUi.Height - 150f, 144f, 150f, true);

                float bubbleX = isLeft ? 45f : 5f;
                const float bubbleY = 368f, bubbleW = 270f, bubbleH = 90f;
                ui.Art(page, "general/dialog/speech_bubble", bubbleX, bubbleY, bubbleW, bubbleH);
                fonts.Draw(page, "white_stroked_small", speaker, bubbleX + bubbleW * 0.5f, bubbleY - 2f, 11f, "center");

                string cleanText = (line.text ?? string.Empty).Replace('|', ' ');
                fonts.DrawLines(page, "black_small", cleanText, bubbleX + 18f, bubbleY + 18f, 10f, bubbleW - 36f, 14f, "left");

                var hintBox = GameUi.Box("TapHint", page, 0f, 0f, GameUi.Width, GameUi.Height);
                dialogTapHint = hintBox.gameObject.AddComponent<CanvasGroup>();
                fonts.Draw(hintBox, "white_stroked_small", "TAP TO CONTINUE", isLeft ? 225f : 95f, 534f, 8f, "center");

                ui.HitBox(page, 0f, 0f, GameUi.Width, GameUi.Height, () =>
                {
                    ui.Sound("cool_button", save);
                    RenderLine();
                });
            }
            RenderLine();
        }
    }
}
