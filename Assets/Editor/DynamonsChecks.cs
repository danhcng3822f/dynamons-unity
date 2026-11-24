using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dynamons
{
    internal static class DynamonsChecks
    {
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
        }

        [MenuItem("Dynamons/Verify Rules")]
        public static void Run()
        {
            var asset = Resources.Load<TextAsset>("Dynamons/gameplay");
            Check(asset != null, "Imported gameplay data must exist");
            var data = JsonUtility.FromJson<GameData>(asset.text);
            Check(data.mons.Length == 37 && data.abilities.Length == 59 && data.mapNodes.Length == 57, "Imported data counts");
            var world = new WorldRules(data, () => .1);
            var save = SaveData.Create(data, new DateTime(2026, 11, 23));
            Check(save.Team.Count == 0 && save.UnlockedPacks.SequenceEqual(new[] { "a" }), "Fresh save starts before starter selection");
            Check(data.tutorial.chooseMons.All(mon => mon.level == 2), "Level 2 starters");
            Check(!world.Available(save).Any(node => node.pack != "a"), "Locked nodes cannot be opened");
            var late = world.Captain(data.mapNodes.First(node => node.pack == "j"), 6);
            Check(late.Members.Length > 0 && late.Members.All(mon => mon.Level >= 17 && mon.Level <= 20), "Late captain levels");
            var tiers = new[] {world.Training("easy"), world.Training("normal"), world.Training("hard")};
            Check(tiers.Select(x => x.Members[0].MaxHp).SequenceEqual(new[] {450, 520, 600}), "Training HP tiers");
            Check(tiers.Select(x => x.Members[0].Sparring).SequenceEqual(new[] {true, true, false}) && tiers[0].Members[0].DamageCap == 26 && tiers[1].Members[0].DamageCap == 42, "Training caps");
            save.Team.Add(OwnedMon.New("krimson", 12, 100));
            Check(world.Arena(save).Members.All(m => m.Level == 13), "Arena scales to the strongest party level with fixed RNG");
            var boss = world.StarterEvent();
            var spec = boss.Members.Single();
            Check(!boss.CanCapture && spec.MonId == "nyxalon" && spec.Level == 40 && spec.MaxHp == 120, "Starter boss identity and health");
            Check(spec.Stats.Value.atk == 95 && spec.Stats.Value.def == 30 && spec.Stats.Value.aim == 200, "Starter boss stat overrides");
            var krimson = OwnedMon.New("krimson", 20, 100);
            var battle = new BattleRules(data, boss, new[] { krimson }, () => .5);
            Check(battle.Damage(battle.Player, battle.Enemy, data.Move("hellfire")) == 25, "Damage into boss DEF 30");
            Check(battle.Damage(battle.Enemy, battle.Player, data.Move("eclipse_nova")) == 12, "Damage from boss ATK 95");
            Check(battle.Apply(new BattleAction("catch"), new SaveData()).Length == 0 && battle.Turn == 1, "Boss cannot be caught");
            Check(battle.Apply(new BattleAction("item", "heal_spray"), new SaveData()).Length == 0 && battle.Turn == 1, "Missing item does not advance turn");
            Check(battle.Apply(new BattleAction("mega")).Length == 1 && battle.Turn == 1 && battle.Apply(new BattleAction("mega")).Length == 0, "One Mega without consuming the turn");
            var duel = new Encounter { Kind = "wild", CanCapture = true, Members = new[] { new MemberSpec { MonId = "squirtle", Level = 14 } } };
            var bench = OwnedMon.New("bulbasaur", 14, 100);
            var practice = new BattleRules(data, duel, new[] { krimson, bench }, () => .5);
            practice.Apply(new BattleAction("ability", "hellfire"));
            Check(practice.Enemy.Shield == 0, "Shell Shield spends its single charge");
            Check(practice.Apply(new BattleAction("ability", "hellfire")).Length == 0, "Cooldown blocks consecutive Hellfire");
            var turn = practice.Turn;
            practice.Apply(new BattleAction("switch", index: 1));
            Check(practice.Player.MonId == "bulbasaur" && practice.Turn == turn, "Switch does not consume a turn");
            var forced = new BattleRules(data, duel, new[] { krimson, bench }, () => .5);
            forced.Player.Hp = 0;
            Check(forced.NeedsSwitch && forced.Apply(new BattleAction("ability", "hellfire")).Length == 0, "Fainted active mon cannot attack");
            forced.Apply(new BattleAction("switch", index: 1));
            Check(!forced.NeedsSwitch && forced.Turn == 1, "Forced replacement costs no turn");
            var bag = new SaveData();
            bag.Add("heal_spray", 1); forced.Player.Hp = 20;
            var healed = forced.Apply(new BattleAction("item", "heal_spray"), bag);
            Check(healed.Any(e => e.Type == "heal" && e.Hp == 70) && bag.Count("heal_spray") == 0, "A usable item heals once and consumes one charge");
            var pair = new Encounter { Members = new[] { new MemberSpec { MonId = "leafrog", Level = 1 }, new MemberSpec { MonId = "lavapede", Level = 1 } } };
            var ordered = new BattleRules(data, pair, new[] { krimson }, () => .5);
            string outgoing = ordered.Enemy.Id;
            ordered.Enemy.Hp = 1;
            var replay = ordered.Apply(new BattleAction("ability", "hellfire"));
            int ko = Array.FindIndex(replay, e => e.Type == "damage" && e.Target == outgoing && e.Hp == 0);
            int swap = Array.FindIndex(replay, e => e.Type == "switch" && e.Target == ordered.Enemy.Id);
            Check(ko >= 0 && swap > ko, "Replay damage targets the outgoing foe before switch");
            var fixtureDay = new DateTime(2026, 11, 23);
            var flow = new GameFlow(data, SaveData.Create(data, fixtureDay), () => .5);
            Check(flow.ChooseStarter("krimson") && !flow.ChooseStarter("malakite") && flow.Save.Team.Single().Level == 2, "Starter choice is exactly once");
            Check(!flow.MoveTo(6), "Cannot travel to a locked pack");
            Check(flow.Save.Event.Sync(fixtureDay.AddDays(1)) && flow.Save.Event.Tickets == 2, "One local daily ticket");
            for (int i = 0; i < 3; i++) Check(flow.Save.Event.TryNpcDrop(fixtureDay.AddDays(1), () => .1), "NPC reward in cap");
            Check(!flow.Save.Event.TryNpcDrop(fixtureDay.AddDays(1), () => .1), "Fourth daily drop rejected");
            var entry = flow.StartBattle(boss, fixtureDay.AddDays(1));
            Check(entry != null && flow.Save.Event.Tickets == 4 && entry.Enemy.Hp == 120, "Boss entry spends exactly one ticket");
            var wiz = entry.Player;
            wiz.Base.atk = 150; entry.Enemy.Hp = 1;
            entry.Apply(new BattleAction("ability", wiz.Moves.First(move => move.power > 0).id));
            Check(entry.Outcome == "player", "Controlled terminal battle fixture");
            flow.CommitBattle(entry, fixtureDay.AddDays(1));
            flow.CommitBattle(entry, fixtureDay.AddDays(1));
            Check(flow.Save.Event.RewardPending && flow.Save.Event.Wins == 1, "Terminal outcome persists one entitlement");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dynamons-check-" + Guid.NewGuid().ToString("N"), "save.json");
            try
            {
                SaveStore.Save(path, flow.Save, data);
                var reopened = new GameFlow(data, SaveStore.Load(path, data), () => .5);
                Check(reopened.Save.Event.RewardPending, "Unclaimed reward survives reopen");
                Check(reopened.ClaimReward("squirtle") && !reopened.ClaimReward("charmander") && reopened.Save.Team.Single(m => m.MonId == "squirtle").Level == 10, "Choose exactly one LV 10 starter");
                SaveStore.Save(path, reopened.Save, data);
                var claimed = new GameFlow(data, SaveStore.Load(path, data), () => .5);
                Check(!claimed.ClaimReward("charmander") && claimed.Save.Team.Count(m => m.MonId == "squirtle") == 1, "Claimed save cannot claim again after normal reopen");
                System.IO.File.WriteAllText(path, "broken");
                var recovered = SaveStore.Load(path, data);
                Check(recovered.Event.RewardPending, "Corrupt JSON recovers last-good backup");
                SaveStore.Save(path, recovered, data);
                System.IO.File.WriteAllText(path, "broken again");
                Check(SaveStore.Load(path, data).Event.RewardPending, "A write after corruption preserves the last-good backup");
                System.IO.Directory.CreateDirectory(path + ".tmp");
                try { SaveStore.Save(path, reopened.Save, data); throw new InvalidOperationException("Expected failed write"); }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
                Check(SaveStore.Load(path, data).Event.RewardPending, "Failed write cannot destroy the last-good backup");
            }
            finally { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true); }

            // Parity checks for visual/HUD/FX schema, wild pool filtering, pack unlocks & rich BattleEvents
            Check(data.fonts.Length == 7, "All 7 bitmap fonts exported");
            foreach (var font in data.fonts)
            {
                Check(font.glyphs != null && font.glyphs.Length > 0, "Font glyphs exported: " + font.id);
                Check(font.kerns != null && font.kerns.Length > 0, "Font kerns exported: " + font.id);
            }
            var bigFont = data.Font("white_stroked_big");
            Check(bigFont != null && bigFont.kerns.Any(k => k.left == 48 && k.right == 50 && Math.Abs(k.adjust - (-3.5f)) < 0.01f),
                "Fractional bitmap font kerning preserved on white_stroked_big");
            Check(data.shopMessage == "I wonder what you'll get!", "Shop message imported");
            Check(data.products.Length == 2 && data.products[1].lootTitle == "contains" && data.products[1].displayRewards?.Length == 4,
                "Shop product metadata imported");

            var wildWorld = new WorldRules(data, () => .99);
            var wild22 = wildWorld.Wild(data.Node(22), 4);
            Check(wild22.Members.Length == 1 && wild22.Members[0].MonId == "kobalt",
                "Wild encounter includes node.alsoAvailMons with onlyFor == wild");
            var cap22 = wildWorld.Captain(data.Node(22), 4);
            Check(cap22.Members.Length > 0 && cap22.Members.All(m => m.MonId != "kobalt"),
                "Captain encounter excludes wild-only pool entries");

            var unlockSave = SaveData.Create(data, fixtureDay);
            unlockSave.Team.Add(OwnedMon.New("krimson", 20, 100));
            unlockSave.NodeId = 10;
            var unlockFlow = new GameFlow(data, unlockSave, () => .5);
            var unlockNode = data.Node(10);
            Check(unlockNode != null, "Fixture node 10 exists");
            var unlockBattle = unlockFlow.StartBattle(new Encounter
            {
                Kind = "boss", Node = unlockNode,
                Members = new[] { new MemberSpec { MonId = "leafrog", Level = 1 } },
            }, fixtureDay);
            Check(unlockBattle != null, "StartBattle succeeds on available node 10");
            unlockBattle.Player.Base.aim = 120;
            unlockBattle.Enemy.Hp = 1;
            unlockBattle.Apply(new BattleAction("ability", "hellfire"));
            unlockFlow.CommitBattle(unlockBattle, fixtureDay);
            Check(unlockFlow.Save.UnlockedPacks.Contains("b") && unlockFlow.Save.UnlockedPacks.Contains("c"),
                "CommitBattle records both node.pack and node.unlocksPack");

            var shieldFoe = new Encounter { Kind = "wild", CanCapture = true, Members = new[] { new MemberSpec { MonId = "squirtle", Level = 14 } } };
            var evBattle = new BattleRules(data, shieldFoe, new[] { OwnedMon.New("krimson", 20, 100) }, () => .5);
            var shieldEvs = evBattle.Apply(new BattleAction("ability", "hellfire"));
            var abilityEv = shieldEvs.FirstOrDefault(e => e.Type == "ability");
            var immuneEv = shieldEvs.FirstOrDefault(e => e.Type == "immune");
            Check(abilityEv != null && abilityEv.AbilityId == "hellfire" && abilityEv.Side == "player", "Ability event structured metadata");
            Check(immuneEv != null && immuneEv.Variant == "shield" && immuneEv.ShieldHits == 0, "Shield immune event structured metadata");

            var voidFoe = new Encounter { Kind = "wild", CanCapture = true, Members = new[] { new MemberSpec { MonId = "hyperbug", Level = 20 } } };
            var voidBattle = new BattleRules(data, voidFoe, new[] { OwnedMon.New("krimson", 20, 100) }, () => .01);
            voidBattle.Player.Base.aim = 120;
            var voidEvs = voidBattle.Apply(new BattleAction("ability", "hellfire"));
            var voidEv = voidEvs.FirstOrDefault(e => e.Type == "immune");
            Check(voidEv != null && voidEv.Variant == "void" && voidEv.Stacks == 1 && voidEv.BonusPct > 0, "Void immune event structured metadata");

            var catchBattle = new BattleRules(data, shieldFoe, new[] { OwnedMon.New("krimson", 20, 100) }, () => 0.0);
            catchBattle.Enemy.Hp = 1;
            var catchBag = new SaveData(); catchBag.Add("discatch", 1);
            var catchEvs = catchBattle.Apply(new BattleAction("catch"), catchBag);
            Check(catchEvs.Any(e => e.Type == "catch" && e.Success), "Catch event Success flag populated");

            foreach (var texPath in new[]
            {
                "Dynamons/Art/general/huds/pane_big_fire", "Dynamons/Art/session/abilities/fx/blast",
                "Dynamons/Art/fonts/white_stroked_big/c65", "Dynamons/Art/shop/products/product_chest"
            })
            {
                Check(Resources.Load<Texture2D>(texPath) != null, "Required texture asset loads: " + texPath);
            }

            Debug.Log("Dynamons rules & parity checks passed.");
        }
    }
}
