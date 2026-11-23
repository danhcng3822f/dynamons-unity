using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private RectTransform mapLayer, mapAvatar;
        private float mapOffset, minOffset;
        private bool walking;
        private UnityEngine.UI.Text mapTicketLabel, modalTicketLabel;
        private UnityEngine.UI.Button modalTicketBattle;
        private void ShowMap()
        {
            walking = false;
            if (save.Event.Sync(DateTime.Today)) Persist();
            if (save.Event.RewardPending) { Choose(true); return; }
            BeginPage(null); Track("music_map_loop");
            var rolls = flow.World.RollMap(save);
            var visibility = flow.World.Visibility(save);
            minOffset = Mathf.Min(0, 320 - visibility.scrollMax);
            mapLayer = GameUi.Box("World map", page, 0, 0, 1355, 568);
            var drag = mapLayer.gameObject.AddComponent<MapDrag>(); drag.Frame = ui.Frame; drag.Move = delta => { if (!walking && overlay == null) Pan(mapOffset + delta); };
            var widths = new[] {395, 320, 320, 395}; int tileX = -75;
            for (int i = 0; i < widths.Length; i++)
            {
                ui.Art(mapLayer, "map/bgs/bg" + (i + 1), tileX, 0, widths[i], 568).raycastTarget = true;
                tileX += widths[i];
            }
            var visible = data.mapNodes.Where(node => visibility.visible.Contains(node.pack) || save.UnlockedPacks.Contains(node.pack)).ToArray();
            foreach (var node in visible)
            {
                var previous = data.Node(node.connect);
                if (previous == null || !visible.Contains(previous)) continue;
                var a = new Vector2(previous.pos[0], previous.pos[1]); var b = new Vector2(node.pos[0], node.pos[1]);
                int dots = Mathf.CeilToInt(Vector2.Distance(a, b) / 13);
                for (int i = 1; i < dots; i++)
                {
                    var at = Vector2.Lerp(a, b, (float)i / dots);
                    ui.Art(mapLayer, "map/path_dot", at.x - 3, at.y - 3, 6, 6);
                }
            }
            foreach (var node in visible)
            {
                rolls.TryGetValue(node.id, out var encounter);
                int x = node.pos[0], y = node.pos[1];
                if (encounter == null) { ui.Art(mapLayer, "map/node", x - 18, y - 16, 36, 32, true).color = new Color(1, 1, 1, .4f); continue; }
                var button = ui.Button(mapLayer, "", x - 22, y - 34, 44, 54, () => Travel(node, encounter), art: "map/node");
                if (encounter.Members.Length != 0)
                {
                    string marker = encounter.Kind == "arena" ? "map/marker_arena" : "map/marker_battle";
                    ui.Art(button.transform, marker, 6, -2, 32, 30, true);
                    if (encounter.EventNpc) ui.Label(button.transform, "EVENT", -4, -6, 52, 16, 10, GameUi.Gold);
                }
            }
            var current = data.Node(save.NodeId);
            mapAvatar = ui.Art(mapLayer, "map/avatar", current.pos[0] - 15, current.pos[1] - 31, 30, 33, true).rectTransform;
            Pan(160 - current.pos[0]);
            GameUi.Fill(page, GameUi.Ink, 0, 0, 320, 42);
            ui.Label(page, "COINS  " + save.Coins, 7, 1, 127, 40, 15, GameUi.Gold);
            ui.Button(page, "SETTINGS", 232, 4, 84, 33, () => { if (!walking) Settings(); });
            ui.Button(page, "FREE HEAL", 6, 47, 88, 34, () => { if (walking) return; flow.HealAll(); PersistThen(() => ui.Sound("use_healing_kit", save)); });
            mapTicketLabel = ui.Button(page, "STARTER EVENT  " + save.Event.Tickets, 155, 47, 159, 34, OpenStarterEvent).GetComponentInChildren<UnityEngine.UI.Text>();
            GameUi.Fill(page, GameUi.Ink, 0, 514, 320, 54);
            ui.Button(page, "SHOP", 6, 521, 98, 39, () => { if (!walking) ShowShop(); });
            ui.Button(page, "ITEMS", 111, 521, 98, 39, () => { if (!walking) ShowInventory(); });
            ui.Button(page, "TEAM", 216, 521, 98, 39, () => { if (!walking) ShowTeam(); });
        }

        private void Pan(float position)
        {
            mapOffset = Mathf.Clamp(position, minOffset, 0);
            mapLayer.anchoredPosition = new Vector2(mapOffset, 0);
        }
        private void Travel(MapNode node, Encounter encounter)
        {
            if (walking || overlay != null || flow.World.Path(save, node.id).Length == 0) return;
            StartCoroutine(Walk(node, encounter));
        }
        private IEnumerator Walk(MapNode node, Encounter encounter)
        {
            walking = true;
            foreach (int id in flow.World.Path(save, node.id).Skip(1))
            {
                var target = data.Node(id);
                Vector2 from = mapAvatar.anchoredPosition, to = new Vector2(target.pos[0] - 15, -(target.pos[1] - 31));
                float start = mapOffset;
                for (float t = 0; t < 1; t += Time.unscaledDeltaTime * 5)
                {
                    mapAvatar.anchoredPosition = Vector2.Lerp(from, to, t);
                    Pan(Mathf.Lerp(start, 160 - target.pos[0], t)); yield return null;
                }
                mapAvatar.anchoredPosition = to; Pan(160 - target.pos[0]);
            }
            flow.MoveTo(node.id); Persist(); walking = false;
            if (encounter.Members.Length != 0) EnemyPreview(encounter);
        }
        private void EnemyPreview(Encounter encounter)
        {
            var panel = OpenOverlay(encounter.Kind == "training" ? "TRAINING BATTLE" : encounter.Kind.ToUpperInvariant() + " ENCOUNTER", 312);
            if (!string.IsNullOrEmpty(encounter.Captain)) ui.Art(panel, "general/enemies/" + encounter.Captain + "_icon", 12, 45, 43, 43, true);
            ui.Label(panel, encounter.Taunt ?? "Ready to fight?", 60, 41, 220, 52, 14);
            for (int i = 0; i < encounter.Members.Length; i++)
            {
                var mon = encounter.Members[i]; int x = 8 + i * 94;
                ui.Art(panel, GameUi.Mon(mon.MonId, "front"), x, 105, 92, 82, true);
                ui.Label(panel, $"LV {mon.Level}  •  {(mon.MaxHp > 0 ? mon.MaxHp : data.battleData.maxHP)} HP", x, 188, 92, 21, 11);
            }
            if (encounter.Kind == "training")
            {
                var tiers = new[] {"easy", "normal", "hard"};
                for (int i = 0; i < tiers.Length; i++)
                {
                    string tier = tiers[i];
                    ui.Button(panel, tier.ToUpperInvariant(), 8 + i * 94, 219, 89, 30, () => EnemyPreview(flow.World.Training(tier)), tier != encounter.Tier);
                }
            }
            else if (encounter.EventNpc) ui.Label(panel, "EVENT  •  CHANCE TO EARN A TICKET", 8, 218, 280, 28, 12, GameUi.Gold);
            ui.Button(panel, "BACK", 10, 261, 124, 37, CloseOverlay);
            ui.Button(panel, "BATTLE", 146, 261, 140, 37, () => BeginBattle(encounter));
        }
        private void OpenStarterEvent()
        {
            if (walking) return;
            if (save.Event.Sync(DateTime.Today)) Persist();
            var panel = OpenOverlay("STARTER EVENT", 296);
            ui.Art(panel, GameUi.Mon("nyxalon", "front"), 87, 43, 122, 132, true).color = new Color(.32f, .25f, .4f, 1);
            ui.Label(panel, "MYSTERY DYNAMON LV 40", 12, 178, 272, 25, 15);
            modalTicketLabel = ui.Label(panel, "", 8, 205, 280, 31, 13, GameUi.Gold);
            ui.Button(panel, "BACK", 10, 245, 120, 37, CloseOverlay);
            modalTicketBattle = ui.Button(panel, "BATTLE: 1 TICKET", 139, 245, 146, 37, () => BeginBattle(flow.World.StarterEvent()));
            RefreshTickets();
        }
        private void RefreshTickets()
        {
            if (mapTicketLabel != null) mapTicketLabel.text = "STARTER EVENT  " + save.Event.Tickets;
            if (modalTicketLabel != null) modalTicketLabel.text = $"TICKETS: {save.Event.Tickets}  •  NPC DROPS: {save.Event.NpcDrops}/3";
            if (modalTicketBattle != null) modalTicketBattle.interactable = save.Event.Tickets > 0;
        }
    }
}
