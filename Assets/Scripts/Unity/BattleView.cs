using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private BattleRules battle;
        private bool animating;
        private RawImage allyArt, foeArt;
        private Text fightLog, allyLabel, foeLabel;
        private RectTransform controls;
        private Image allyHealth, foeHealth;
        private string shownAlly, shownFoe;

        private void BeginBattle(Encounter encounter)
        {
            var startDialog = encounter.Dialogs?.dialog_start;
            if (!string.IsNullOrEmpty(startDialog) && !save.SeenDialogs.Contains(startDialog) && data.Dialog(startDialog)?.content?.Length > 0)
            { Dialogue(startDialog, () => BeginBattle(encounter)); return; }
            battle = flow.StartBattle(encounter, DateTime.Today);
            if (battle == null) { ShowMap(); Note(flow.LivingParty().Length == 0 ? "Heal your party first." : "This battle is not available."); return; }
            if (encounter.Kind == "starter_event" && !Persist())
            {
                flow.CancelEntry(battle); battle = null; ShowMap(); Note("Could not save the ticket. Please try again."); return;
            }
            BeginPage("session/bgs/" + encounter.Background);
            allyArt = null; foeArt = null; allyLabel = null; foeLabel = null;
            Track(encounter.Background == "arena" ? "music_arena_loop" : "music_battle_loop");
            ui.Sound(encounter.Kind == "boss" ? "music_battle_boss_start" : "music_battle_start", save);
            fightLog = ui.Label(page, "BATTLE START!", 55, 4, 210, 59, 15, GameUi.Gold);
            ui.Button(page, "SET", 267, 7, 47, 33, () => { if (!animating) Settings(); });
            PaintCombatants();
            controls = GameUi.Box("Battle controls", page, 0, 436, 320, 132);
            DrawCombatControls();
        }

        private void PaintCombatants(Combatant ally = null, Combatant foe = null)
        {
            ally ??= battle.Player; foe ??= battle.Enemy;
            if (shownFoe != foe.Id || foeArt == null)
            {
                if (foeArt != null) { foeArt.gameObject.SetActive(false); Destroy(foeArt.gameObject); }
                shownFoe = foe.Id;
                foeArt = ui.Art(page, GameUi.Mon(foe.MonId, "front"), 158, 122, 153, 152, true);
            }
            if (shownAlly != ally.Id || allyArt == null)
            {
                if (allyArt != null) { allyArt.gameObject.SetActive(false); Destroy(allyArt.gameObject); }
                shownAlly = ally.Id;
                allyArt = ui.Art(page, GameUi.Mon(ally.MonId, "back"), 13, 262, 148, 156, true);
            }
            if (foeLabel == null)
            {
                GameUi.Fill(page, GameUi.Ink, 9, 75, 148, 44);
                foeLabel = ui.Label(page, "", 11, 77, 144, 24, 12);
                foeHealth = GameUi.Fill(page, GameUi.Green, 15, 105, 136, 7);
            }
            if (allyLabel == null)
            {
                GameUi.Fill(page, GameUi.Ink, 161, 379, 150, 44);
                allyLabel = ui.Label(page, "", 163, 381, 146, 24, 12);
                allyHealth = GameUi.Fill(page, GameUi.Green, 167, 409, 138, 7);
            }
            DrawHp(foe, foe.Hp); DrawHp(ally, ally.Hp);
        }

        private void DrawHp(Combatant mon, int hp)
        {
            bool enemy = mon.Id == shownFoe;
            var label = enemy ? foeLabel : allyLabel;
            var bar = enemy ? foeHealth : allyHealth;
            label.text = $"{data.Mon(mon.MonId).title}  LV {mon.Level}\n{hp}/{mon.MaxHp} HP";
            bar.rectTransform.sizeDelta = new Vector2((enemy ? 136 : 138) * hp / mon.MaxHp, 7);
            label.color = mon.Mega ? GameUi.Gold : Color.white;
        }

        private void DrawCombatControls()
        {
            GameUi.Clear(controls); GameUi.Fill(controls, GameUi.Ink, 0, 0, 320, 132);
            var mon = battle.Player;
            string passive = mon.Passive?.type switch {
                "charge" => $"SPORE {mon.Charges}/{mon.Passive.threshold}", "ramp" => $"BLAZE {mon.PowerStacks}",
                "shield" => "SHELL " + mon.Shield, _ => mon.Passive?.chance > 0 ? "VOID " + mon.VoidStacks : "" };
            ui.Label(controls, (mon.Status ?? "").ToUpperInvariant() + "  " + passive, 4, 0, 312, 20, 11, GameUi.Gold);
            for (int i = 0; i < mon.Moves.Length; i++)
            {
                var move = mon.Moves[i]; int x = 4 + i * 63;
                bool use = !battle.NeedsSwitch && mon.Ready(move);
                var button = ui.Button(controls, move.title, x, 22, 60, 52, () => BattleClick(new BattleAction("ability", move.id)), use, GameUi.Move(move.id));
                if (!use && mon.Cooldowns.TryGetValue(move.id, out int turns) && turns > 0) ui.Label(button.transform, turns.ToString(), 24, 20, 32, 26, 19);
                ui.Label(controls, move.title, x, 75, 60, 21, 8);
            }
            ui.Button(controls, "MEGA", 4, 98, 62, 31, () => BattleClick(new BattleAction("mega")), battle.CanMega);
            ui.Button(controls, "ITEMS", 69, 98, 62, 31, () => BattleInventory(), !battle.NeedsSwitch);
            ui.Button(controls, "SWAP", 134, 98, 62, 31, OpenSwitch, battle.Party.Count(m => m.Hp > 0) > 1 || battle.NeedsSwitch);
            ui.Button(controls, "CATCH", 199, 98, 59, 31, () => BattleClick(new BattleAction("catch")), !battle.NeedsSwitch && battle.Encounter.CanCapture && save.Count("discatch") > 0);
            ui.Button(controls, "RUN", 261, 98, 55, 31, () => BattleClick(new BattleAction("escape")));
        }

        private void OpenSwitch()
        {
            var panel = OpenOverlay(battle.NeedsSwitch ? "CHOOSE A DYNAMON" : "SWITCH DYNAMON", 230);
            for (int i = 0; i < battle.Party.Length; i++)
            {
                int slot = i; var mon = battle.Party[i];
                ui.Button(panel, $"{data.Mon(mon.MonId).title}   {mon.Hp}/{mon.MaxHp} HP", 11, 42 + i * 43, 274, 37,
                    () => BattleClick(new BattleAction("switch", index: slot)), i != battle.PlayerSlot && mon.Hp > 0);
            }
            ui.Button(panel, "BACK", 96, 181, 104, 36, CloseOverlay);
        }
        private void BattleInventory(int offset = 0)
        {
            var panel = OpenOverlay("BATTLE ITEMS", 308);
            var items = save.Inventory.Where(row => row.id != "discatch" && data.Item(row.id)?.inBattle == true).ToArray();
            foreach (var row in items.Skip(offset).Take(5).Select((item, i) => (item, i)))
            {
                var item = row.item;
                ui.Button(panel, data.Item(item.id).title + "  x" + item.amount, 9, 42 + row.i * 41, 278, 35,
                    () => BattleClick(new BattleAction("item", item.id)), !(battle.Player.Status == "sleep" && item.id.StartsWith("can_", StringComparison.Ordinal)));
            }
            if (items.Length == 0) ui.Label(panel, "NO BATTLE ITEMS", 12, 82, 272, 35, 14);
            ui.Button(panel, "PREV", 8, 256, 85, 36, () => BattleInventory(offset - 5), offset > 0);
            ui.Button(panel, "BACK", 104, 256, 88, 36, CloseOverlay);
            ui.Button(panel, "NEXT", 204, 256, 84, 36, () => BattleInventory(offset + 5), items.Length > offset + 5);
        }

        private void BattleClick(BattleAction action)
        {
            if (animating) return;
            var steps = battle.Apply(action, save);
            if (steps.Length == 0) return;
            CloseOverlay();
            // Commit terminal rewards before playing FX; closing during the KO cannot lose the choice.
            Reward[] loot = battle.Outcome == null ? Array.Empty<Reward>() : flow.CommitBattle(battle, DateTime.Today);
            bool needsSave = action.Type == "item" || action.Type == "catch" || battle.Outcome != null;
            bool saved = !needsSave || Persist();
            StartCoroutine(AnimateBattle(steps, loot, saved));
        }

        private IEnumerator AnimateBattle(BattleEvent[] steps, Reward[] loot, bool saved)
        {
            animating = true; controls.gameObject.SetActive(false);
            foreach (var step in steps)
            {
                if (step.Type == "ability")
                {
                    var sfx = step.Visuals.FirstOrDefault(visual => visual.action.StartsWith("sfx-", StringComparison.Ordinal))?.action;
                    if (sfx != null) ui.Sound("attack_" + sfx.Substring(4), save);
                    else if (step.Visuals.Any(visual => visual.action.StartsWith("heal_spray", StringComparison.Ordinal) || visual.action.StartsWith("antidote", StringComparison.Ordinal))) ui.Sound("spray_powerup", save);
                    else if (step.Visuals.Any(visual => visual.action.StartsWith("atk_pepper", StringComparison.Ordinal) || visual.action.StartsWith("def_berry", StringComparison.Ordinal) || visual.action.StartsWith("aim_shroom", StringComparison.Ordinal))) ui.Sound("eating_powerup", save);
                    else ui.Sound(step.Visuals.Any(visual => visual.action.StartsWith("can_", StringComparison.Ordinal)) ? "canned_powerup" : "attack_scratch", save);
                    if (step.Visuals.Any(visual => visual.action.StartsWith("ram", StringComparison.Ordinal) || visual.action.StartsWith("jump", StringComparison.Ordinal)))
                    {
                        var sprite = step.Actor.StartsWith("enemy-", StringComparison.Ordinal) ? foeArt : allyArt;
                        yield return Bump(sprite, sprite == foeArt ? -14 : 14);
                    }
                }
                if (!string.IsNullOrEmpty(step.Text) && step.Type != "end") fightLog.text = step.Text;
                if (step.Type == "damage" || step.Type == "heal")
                {
                    var mon = step.Target == shownFoe ? battle.Opponents.FirstOrDefault(m => m.Id == shownFoe) : step.Target == shownAlly ? battle.Party.FirstOrDefault(m => m.Id == shownAlly) : null;
                    if (mon != null) DrawHp(mon, step.Hp);
                    fightLog.text = step.Type == "heal" ? "HEALED " + step.Amount + " HP" : "DAMAGE  " + step.Amount;
                    if (step.Type == "damage") yield return Bump(step.Target == shownFoe ? foeArt : allyArt, 7);
                }
                if (step.Type == "stat") ui.Sound(step.Amount > 0 ? "stat_raised" : "stat_lowered", save);
                if (step.Type == "faint") ui.Sound("faint", save);
                if (step.Type == "switch")
                {
                    var ally = battle.Party.FirstOrDefault(mon => mon.Id == step.Target) ?? battle.Party.FirstOrDefault(mon => mon.Id == shownAlly);
                    var foe = battle.Opponents.FirstOrDefault(mon => mon.Id == step.Target) ?? battle.Opponents.FirstOrDefault(mon => mon.Id == shownFoe);
                    PaintCombatants(ally, foe);
                }
                if (step.Type != "end" && (step.Type == "ability" || step.Text != null || step.Type == "damage")) yield return new WaitForSecondsRealtime(.24f / save.Speed);
            }
            animating = false;
            if (battle.Outcome != null) { Result(loot, saved); yield break; }
            PaintCombatants(); controls.gameObject.SetActive(true); DrawCombatControls();
            if (!saved) Note("Could not save progress. Retry after freeing storage.");
            else if (battle.NeedsSwitch) OpenSwitch();
        }

        private IEnumerator Bump(RawImage sprite, float offset)
        {
            var transform = sprite.rectTransform;
            var original = transform.anchoredPosition;
            float phase = 0;
            while (phase < 1)
            {
                phase += Time.unscaledDeltaTime * 7 * save.Speed;
                transform.anchoredPosition = original + new Vector2(Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI) * offset, 0);
                yield return null;
            }
            transform.anchoredPosition = original;
        }

        private void Result(Reward[] loot, bool saved)
        {
            string outcome = battle.Outcome;
            string after = outcome == "player" ? battle.Encounter.Dialogs?.dialog_win : outcome == "enemy" ? battle.Encounter.Dialogs?.dialog_lose : null;
            BeginPage("session/bgs/water"); Track("music_map_loop");
            GameUi.Fill(page, new Color(0, 0, 0, .63f), 0, 0, 320, 568);
            Header(outcome == "player" ? "YOU WIN" : outcome == "enemy" ? "DEFEAT" : "ESCAPED");
            if (battle.Caught != null)
            {
                ui.Art(page, GameUi.Mon(battle.Caught.MonId, "front"), 91, 80, 138, 140, true);
                ui.Label(page, "CAUGHT " + data.Mon(battle.Caught.MonId).title, 8, 221, 304, 25, 17, GameUi.Gold);
            }
            else
            {
                ui.Art(page, GameUi.Mon(battle.Player.MonId, "front"), 91, 74, 138, 142, true);
                ui.Label(page, $"{data.Mon(battle.Player.MonId).title}   XP  {battle.Player.Xp}", 8, 220, 304, 28, 15);
            }
            for (int i = 0; i < loot.Length; i++)
            {
                var reward = loot[i]; int x = 10 + i % 5 * 61, y = 264 + i / 5 * 67;
                ui.Art(page, GameUi.Item(reward.id), x + 7, y, 46, 40, true);
                ui.Label(page, "x" + reward.amount, x, y + 39, 60, 21, 13);
            }
            if (save.Event.RewardPending) ui.Label(page, "CHOOSE A LV 10 STARTER", 8, 363, 304, 36, 16, GameUi.Gold);
            if (!saved) ui.Label(page, "SAVE FAILED — CONTINUE TO RETRY", 8, 434, 304, 36, 12, GameUi.Gold);
            ui.Button(page, "CONTINUE", 80, 498, 160, 42, () => {
                if (!saved && !Persist()) { Note("Could not save. Please retry."); return; }
                battle = null;
                Dialogue(after, () => { if (outcome == "enemy") ShowTeam(); else ShowMap(); });
            });
        }
    }
}
