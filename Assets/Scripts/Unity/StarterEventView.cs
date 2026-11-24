using System;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private const int StarterEventLevel = 40;
        private const int StarterRewardLevel = 10;
        private RectTransform starterModalDynamic;

        private void OpenStarterEventModal()
        {
            if (walking) return;
            if (save.Event.Sync(DateTime.Today)) Persist();
            ui.Sound("screen_open", save);
            OpenOverlayShell(0.72f);

            var card = VectorCanvas.Create("StarterEventCard", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            card.FillRoundRect(22f, 115f, 276f, 338f, 16f, GameUi.HexColor("#e8b35a"));
            card.FillRoundRect(25f, 118f, 270f, 332f, 14f, GameUi.HexColor("#231a38"));
            card.FillRoundRect(44f, 156f, 232f, 162f, 18f, GameUi.HexColor("#3a245a"));
            card.FillCircle(160f, 229f, 58f, new Color32(183, 117, 255, 95), new Color32(183, 117, 255, 0), 28);
            card.Commit();

            fonts.Draw(overlay, "white_stroked_big", "STARTER EVENT", 160f, 130f, 17f, "center");
            var silhouette = ui.Art(overlay, GameUi.Mon("nyxalon", "front"), 83f, 157f, 154f, 145f, true);
            silhouette.color = new Color(0.13f, 0.13f, 0.13f, 1f);
            fonts.Draw(overlay, "white_stroked_big", "???", 160f, 212f, 29f, "center");
            fonts.Draw(overlay, "white_stroked_small", $"MYSTERY DYNAMON LV {StarterEventLevel}", 160f, 300f, 10f, "center");
            fonts.Draw(overlay, "white_stroked_small", $"WIN: PICK ONE LV {StarterRewardLevel} STARTER", 160f, 328f, 10f, "center");
            fonts.Draw(overlay, "white_stroked_small", "CHARMANDER / BULBASAUR / SQUIRTLE", 160f, 344f, 8f, "center");
            ui.Art(overlay, GameUi.Item("starter_ticket"), 47f, 363f, 28f, 22f, true);

            starterModalDynamic = GameUi.Box("StarterEventDynamic", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            RefreshStarterModalDynamic();

            ui.ArtButton(overlay, "general/btn_close", 253f, 102f, 40f, 40f, () =>
            {
                ui.Sound("screen_close", save);
                starterModalDynamic = null;
                CloseOverlay();
            }, true, 1f, "CloseStarterModal");
        }

        private void RefreshStarterModalDynamic()
        {
            if (starterModalDynamic == null) return;
            GameUi.Clear(starterModalDynamic);

            int tickets = save.Event.Tickets;
            int drops = save.Event.NpcDrops;
            bool canFight = tickets > 0 && flow.LivingParty().Length > 0;

            fonts.Draw(starterModalDynamic, "white_stroked_small", $"TICKETS: {tickets}", 79f, 368f, 10f, "left");
            fonts.Draw(starterModalDynamic, "white_stroked_small", $"NPC TODAY: {drops}/3", 265f, 368f, 10f, "right");
            if (!canFight)
            {
                string hint = tickets > 0 ? "HEAL YOUR TEAM TO FIGHT" : "WIN EVENT NPC BATTLES FOR TICKETS";
                fonts.Draw(starterModalDynamic, "white_stroked_small", hint, 160f, 388f, 9f, "center");
            }

            var btnVec = VectorCanvas.Create("FightBtnBg", starterModalDynamic, 0f, 0f, GameUi.Width, GameUi.Height);
            btnVec.FillRoundRect(82f, 408f, 156f, 32f, 8f, GameUi.HexColor("#a64f37", canFight ? 1f : 0.45f));
            btnVec.Commit();

            fonts.Draw(starterModalDynamic, "white_stroked_small", "FIGHT (1 TICKET)", 160f, 419f, 11f, "center",
                canFight ? Color.white : new Color(1f, 1f, 1f, 0.45f));

            ui.HitBox(starterModalDynamic, 82f, 408f, 156f, 32f, () =>
            {
                if (!canFight) { ui.Sound("stat_lowered", save); return; }
                ui.Sound("button_click", save);
                starterModalDynamic = null;
                CloseOverlay();
                BeginBattle(flow.World.StarterEvent());
            });
        }

        private void ShowStarterReward()
        {
            BeginPage(null); Track("music_map_loop");
            ui.Art(page, "map/bgs/bg1", 0f, 0f, GameUi.Width, GameUi.Height);
            GameUi.Fill(page, new Color32(16, 8, 30, 219), 0f, 0f, GameUi.Width, GameUi.Height);

            fonts.Draw(page, "white_stroked_big", "CHOOSE YOUR STARTER", 160f, 27f, 19f, "center", null, 300f);
            fonts.Draw(page, "white_stroked_small", "STARTER EVENT REWARD", 160f, 57f, 11f, "center");

            for (int i = 0; i < WorldRules.StarterRewards.Length; i++)
            {
                string id = WorldRules.StarterRewards[i];
                var mon = data.Mon(id);
                float y = 87f + i * 128f;
                ui.Art(page, $"onboarding/choose_panel_{mon.kind}", 14f, y, 292f, 120f);
                ui.Art(page, GameUi.Mon(id, "front"), 19f, y + 2f, 126f, 116f, true);
                fonts.Draw(page, "white_stroked_big", (mon.title ?? id).ToUpperInvariant(), 221f, y + 23f, 16f, "center", null, 130f);
                fonts.Draw(page, "white_stroked_small", $"LV {StarterRewardLevel}", 221f, y + 46f, 12f, "center");
                if (save.Team.Any(owned => owned.MonId == id))
                    fonts.Draw(page, "white_stroked_small", "OWNED", 221f, y + 63f, 8f, "center");
                ui.ArtButton(page, "onboarding/btn_choose", 219f, y + 80f, 69f, 34f, () => ClaimRewardChoice(id), true, 1f, "Claim_" + id);
            }
        }

        private void ClaimRewardChoice(string id)
        {
            if (!flow.ClaimReward(id)) { ui.Sound("stat_lowered", save); return; }
            if (!Persist())
            {
                save.Team.RemoveAt(Math.Min(save.Team.Count - 1, 3));
                save.Event.RewardPending = true;
                ui.Sound("stat_lowered", save);
                Note("Could not save. Please try again.");
                return;
            }
            ui.Sound("button_click", save);
            ShowMap();
        }
    }
}
