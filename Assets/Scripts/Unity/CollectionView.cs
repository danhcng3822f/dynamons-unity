using System;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private void CollectionPage(string title)
        {
            BeginPage(); Track("music_map_loop");
            GameUi.Fill(page, new Color(0, 0, 0, .76f), 0, 0, 320, 568);
            Header(title);
            ui.Button(page, "MAP", 104, 520, 112, 39, ShowMap);
        }

        private void ShowTeam(int pageIndex = 0)
        {
            pageIndex = GameData.Clamp(pageIndex, 0, Math.Max(0, (save.Team.Count - 1) / 5));
            int offset = pageIndex * 5, selectedPage = pageIndex;
            CollectionPage("YOUR TEAM");
            int owned = save.Team.Select(mon => mon.MonId).Distinct().Count(id => data.Mon(id)?.bossOnly == false);
            ui.Label(page, $"COLLECTION  {owned}/{data.mons.Count(mon => !mon.bossOnly)}", 20, 47, 280, 27, 13, GameUi.Gold);
            for (int i = offset; i < Math.Min(save.Team.Count, offset + 5); i++)
            {
                int index = i, y = 82 + (i - offset) * 72;
                var mon = save.Team[i]; var info = data.Mon(mon.MonId);
                GameUi.Fill(page, GameUi.Panel, 10, y, 300, 65);
                ui.Art(page, GameUi.Mon(mon.MonId, "icon"), 13, y + 6, 54, 54, true);
                ui.Label(page, $"{info.title}  LV {mon.Level}\n{mon.Hp}/{data.battleData.maxHP} HP  •  {(i < 3 ? "PARTY" : "STORAGE")}", 71, y + 5, 166, 51, 12, alignment: TextAnchor.MiddleLeft);
                ui.Button(page, "INFO", 246, y + 16, 55, 33, () => MonDetails(index, selectedPage));
            }
            ui.Button(page, "PREV", 12, 454, 63, 35, () => ShowTeam(selectedPage - 1), pageIndex > 0);
            ui.Button(page, "FREE HEAL", 91, 454, 138, 35, () => { flow.HealAll(); PersistThen(() => { ui.Sound("use_healing_kit", save); ShowTeam(selectedPage); }); });
            ui.Button(page, "NEXT", 245, 454, 63, 35, () => ShowTeam(selectedPage + 1), save.Team.Count > offset + 5);
        }

        private void MonDetails(int index, int teamPage)
        {
            var mon = save.Team[index]; var info = data.Mon(mon.MonId); var stats = data.StatsAt(mon.MonId, mon.Level);
            var panel = OpenOverlay(info.title, 432);
            ui.Art(panel, GameUi.Mon(mon.MonId, "front"), 70, 43, 156, 138, true);
            ui.Label(panel, $"LV {mon.Level}   ATK {stats.atk}   DEF {stats.def}   AIM {stats.aim}", 8, 188, 280, 27, 13);
            int need = flow.NeededXp(mon);
            var stone = "levelup_" + info.kind;
            ui.Label(panel, need > 0 ? $"XP {mon.Xp}/{need}  •  DYNastones {save.Count(stone) + save.Count("levelup_joker")}".ToUpperInvariant() : "MAX LEVEL", 8, 215, 280, 25, 13, GameUi.Gold);
            ui.Label(panel, string.Join("  •  ", data.MovesAt(mon.MonId, mon.Level).Select(move => move.title)), 10, 248, 276, 43, 11);
            ui.Button(panel, "LEVEL UP", 8, 306, 134, 38, () => {
                if (flow.LevelUp(index)) PersistThen(() => { ui.Sound("level_up_animation", save); MonDetails(index, teamPage); });
            }, need > 0 && mon.Xp >= need && (save.Count(stone) > 0 || save.Count("levelup_joker") > 0));
            ui.Button(panel, "REORDER", 151, 306, 137, 38, () => Reorder(index, teamPage), save.Team.Count > 1);
            ui.Button(panel, "RELEASE", 8, 374, 134, 38, () => {
                var confirm = OpenOverlay("RELEASE DYNAMON?", 135);
                ui.Button(confirm, "CANCEL", 12, 73, 125, 36, () => MonDetails(index, teamPage));
                ui.Button(confirm, "RELEASE", 155, 73, 125, 36, () => { if (flow.Release(index)) PersistThen(() => ShowTeam(teamPage)); });
            }, save.Team.Count > 1);
            ui.Button(panel, "BACK", 151, 374, 137, 38, CloseOverlay);
        }
        private void Reorder(int index, int teamPage)
        {
            var panel = OpenOverlay("SWAP WITH PARTY SLOT", 246);
            for (int i = 0; i < Math.Min(3, save.Team.Count); i++)
            {
                int slot = i;
                ui.Button(panel, $"{i + 1}  {data.Mon(save.Team[i].MonId).title}", 12, 48 + i * 44, 272, 36,
                    () => { if (flow.Swap(index, slot)) PersistThen(() => ShowTeam(teamPage)); }, i != index);
            }
            ui.Button(panel, "BACK", 94, 196, 112, 34, () => MonDetails(index, teamPage));
        }
        private void ShowInventory(int offset = 0)
        {
            CollectionPage("INVENTORY");
            var inventory = save.Inventory.ToArray();
            offset = GameData.Clamp(offset, 0, 6 * Math.Max(0, (inventory.Length - 1) / 6));
            int first = offset;
            for (int i = offset; i < Math.Min(inventory.Length, offset + 6); i++)
            {
                var row = inventory[i]; var item = data.Item(row.id); int y = 64 + (i - offset) * 65;
                if (item == null) continue;
                GameUi.Fill(page, GameUi.Panel, 10, y, 300, 60);
                ui.Art(page, GameUi.Item(item.id), 14, y + 5, 52, 49, true);
                ui.Label(page, $"{item.title}  x{row.amount}", 77, y + 5, 162, 49, 13, alignment: TextAnchor.MiddleLeft);
                ui.Button(page, item.inBattle ? "INFO" : "USE", 239, y + 13, 64, 34, () => {
                    if (item.inBattle) { Note(item.desc?.Replace('|', ' ') ?? "Use during battle."); return; }
                    if (flow.UseItem(item.id)) PersistThen(() => ShowInventory(first));
                    else Note("No Dynamon can use this item yet.");
                });
            }
            if (inventory.Length == 0) ui.Label(page, "NO ITEMS", 10, 94, 300, 35, 17);
            ui.Button(page, "PREV", 12, 462, 78, 34, () => ShowInventory(first - 6), first > 0);
            ui.Button(page, "NEXT", 230, 462, 78, 34, () => ShowInventory(first + 6), inventory.Length > first + 6);
        }
        private void ShowShop()
        {
            CollectionPage("COIN SHOP");
            ui.Label(page, "COINS  " + save.Coins, 20, 55, 280, 30, 16, GameUi.Gold);
            for (int i = 0; i < data.products.Length; i++)
            {
                var product = data.products[i]; int y = 105 + i * 142;
                GameUi.Fill(page, GameUi.Panel, 12, y, 296, 125);
                ui.Art(page, "iap-shop/products/product_" + (product.id == "chest" ? "coolchest" : product.id), 17, y + 12, 93, 97, true);
                ui.Label(page, product.title, 113, y + 13, 187, 32, 16);
                ui.Button(page, "BUY  " + product.price + " COINS", 122, y + 63, 169, 40, () => BuyLoot(product.id), save.Coins >= product.price);
            }
        }
        private void BuyLoot(string id)
        {
            var loot = flow.Buy(id);
            if (loot.Length == 0) return;
            PersistThen(() => ShowLoot(id, loot));
        }
        private void ShowLoot(string id, Reward[] loot)
        {
            ShowShop();
            ui.Sound(id == "chest" ? "chest_opening" : "loot_bag_opening", save);
            var panel = OpenOverlay("LOOT", 279);
            for (int i = 0; i < loot.Length; i++)
            {
                var row = loot[i]; int x = 12 + i % 4 * 69, y = 51 + i / 4 * 72;
                ui.Art(panel, GameUi.Item(row.id), x + 7, y, 47, 45, true);
                ui.Label(panel, "x" + row.amount, x, y + 45, 60, 20, 12);
            }
            ui.Button(panel, "CONTINUE", 80, 225, 136, 38, CloseOverlay);
        }
    }
}
