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
        private GameData data => flow.Data;
        private SaveData save => flow.Save;
        private RectTransform page, overlay;
        private string savePath;
        private FontData big;
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
                savePath = Path.Combine(Application.persistentDataPath, "Dynamons2", "save.json");
                var loaded = SaveStore.Load(savePath, gameData);
                var random = new System.Random();
                flow = new GameFlow(gameData, loaded, random.NextDouble);
                big = data.fonts.FirstOrDefault(font => font.id == "white_stroked_big");
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
            GameUi.Clear(ui.Frame); overlay = null;
            page = GameUi.Box("Screen", ui.Frame, 0, 0, 320, 568);
            GameUi.Fill(page, GameUi.Ink, 0, 0, 320, 568);
            if (art != null) ui.Art(page, art, 0, 0, 320, 568);
        }
        private void Track(string name) { musicTrack = name; ui.Music(name, save); }
        private void Header(string name) { ui.Heading(page, big, name, 14); }
        private void Note(string text)
        {
            var panel = OpenOverlay("NOTICE", 172);
            ui.Label(panel, text, 12, 42, 272, 70, 14);
            ui.Button(panel, "OK", 98, 122, 100, 36, CloseOverlay);
        }
        private RectTransform OpenOverlay(string title, int height)
        {
            CloseOverlay();
            var input = page.GetComponent<CanvasGroup>() ?? page.gameObject.AddComponent<CanvasGroup>();
            input.interactable = false; input.blocksRaycasts = false;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            overlay = GameUi.Box("Modal", ui.Frame, 0, 0, 320, 568);
            GameUi.Fill(overlay, new Color(0, 0, 0, .65f), 0, 0, 320, 568, true);
            var panel = GameUi.Box(title, overlay, 12, (568 - height) / 2f, 296, height);
            GameUi.Fill(panel, GameUi.Panel, 0, 0, 296, height, true);
            ui.Label(panel, title, 6, 8, 284, 28, 18, GameUi.Gold);
            return panel;
        }
        private void CloseOverlay()
        {
            if (overlay == null) return;
            overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); overlay = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            var input = page.GetComponent<CanvasGroup>();
            if (input != null) { input.interactable = true; input.blocksRaycasts = true; }
        }

        private void Splash()
        {
            BeginPage(null); Track("music_map_loop");
            ui.Art(page, "splash/splash", 0, -325, 320, 893);
            ui.Art(page, "splash/logo", 34, 14, 252, 100, true);
            ui.Button(page, "PLAY", 102, 336, 116, 48, () => {
                ui.Sound("button_click", save);
                if (save.Team.Count == 0) Choose(); else ShowMap();
            }, art: "splash/btn_play");
        }
        private void Choose(bool reward = false)
        {
            BeginPage(); Track("music_map_loop");
            GameUi.Fill(page, new Color(0, 0, 0, .64f), 0, 0, 320, 568);
            Header(reward ? "CHOOSE YOUR REWARD" : "CHOOSE A DYNAMON");
            var choices = reward ? WorldRules.StarterRewards.Select(id => new Choice { id = id, type = data.Mon(id).kind, level = 10 }).ToArray() : data.tutorial.chooseMons;
            for (int i = 0; i < choices.Length; i++)
            {
                var choice = choices[i]; int x = 6 + i * 104;
                ui.Art(page, "onboarding/choose_panel_" + choice.type, x, 160, 100, 210);
                ui.Label(page, data.Mon(choice.id).title, x, 180, 100, 28, 13);
                ui.Art(page, GameUi.Mon(choice.id, "front"), x + 3, 214, 94, 110, true);
                ui.Label(page, "LV " + choice.level, x, 325, 100, 22, 13);
                ui.Button(page, "CHOOSE", x + 4, 348, 92, 42, () => ChooseMon(choice.id, reward), art: "onboarding/btn_choose");
            }
        }
        private void ChooseMon(string id, bool reward)
        {
            bool ok = reward ? flow.ClaimReward(id) : flow.ChooseStarter(id);
            if (!ok) return;
            if (!Persist())
            {
                if (reward) { save.Team.RemoveAt(Math.Min(save.Team.Count - 1, 3)); save.Event.RewardPending = true; }
                else save.Team.Clear();
                Note("Could not save. Please try again."); return;
            }
            ui.Sound("button_click", save);
            if (reward) ShowMap(); else Dialogue("hello_world", ShowMap);
        }
        private void Dialogue(string id, Action done)
        {
            if (string.IsNullOrEmpty(id) || save.SeenDialogs.Contains(id) || (data.Dialog(id)?.content?.Length ?? 0) == 0) { done(); return; }
            var dialogue = data.Dialog(id); int index = 0;
            void Next()
            {
                if (index >= dialogue.content.Length) { save.SeenDialogs.Add(id); Persist(); done(); return; }
                BeginPage();
                var line = dialogue.content[index++];
                var speaker = line.who?.Split(':') ?? Array.Empty<string>();
                if (speaker.Length > 1) ui.Art(page, "general/dialog/" + speaker[1], speaker[0] == "right" ? 160 : 0, 202, 160, 160, true);
                GameUi.Fill(page, GameUi.Ink, 14, 363, 292, 132);
                ui.Label(page, line.text.Replace('|', '\n'), 28, 375, 264, 106, 16);
                ui.Button(page, "CONTINUE", 94, 508, 132, 40, Next);
            }
            Next();
        }
        private void Settings()
        {
            var panel = OpenOverlay("SETTINGS", 215);
            void Toggle(Action change) { change(); Persist(); Track(musicTrack); Settings(); }
            ui.Button(panel, "SOUND " + (save.Sound ? "ON" : "OFF"), 32, 44, 232, 34, () => Toggle(() => save.Sound = !save.Sound));
            ui.Button(panel, "MUSIC " + (save.Music ? "ON" : "OFF"), 32, 83, 232, 34, () => Toggle(() => save.Music = !save.Music));
            ui.Button(panel, "BATTLE SPEED  " + save.Speed + "X", 32, 122, 232, 34, () => Toggle(() => save.Speed = save.Speed % 3 + 1));
            ui.Button(panel, "CLOSE", 98, 164, 100, 36, CloseOverlay);
        }
    }
}
