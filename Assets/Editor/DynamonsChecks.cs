using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

            var unnormalized = JsonUtility.FromJson<GameData>(asset.text);
            unnormalized.Normalize();
            Check(unnormalized.Mon("krimson").passive == null && unnormalized.Mon("leafrog").passive == null,
                "Normalize clears empty JsonUtility Passive stubs on species without passives");
            Check(GameData.HasPassive(unnormalized.Mon("hyperbug").passive) && GameData.PassiveKey(unnormalized.Mon("hyperbug").passive) == "void",
                "Hyperbug passive preserved with explicit void key even when type is empty");
            foreach (var pMonId in new[] { "squirtle", "charmander", "bulbasaur", "hyperbug" })
            {
                var p = unnormalized.Mon(pMonId).passive;
                Check(GameData.HasPassive(p) && !string.IsNullOrEmpty(GameData.PassiveTitle(p)) && !string.IsNullOrEmpty(GameData.PassiveDesc(p)),
                    "Passive UI title & description populated for " + pMonId);
            }
            var plainCheck = new BattleRules(JsonUtility.FromJson<GameData>(asset.text),
                new Encounter { Kind = "wild", Members = new[] { new MemberSpec { MonId = "leafrog", Level = 4 } } },
                new[] { OwnedMon.New("krimson", 4, 100) }, () => 0.0);
            Check(plainCheck.Player.Passive == null && plainCheck.Enemy.Passive == null,
                "BattleRules strips empty JsonUtility Passive objects cleanly");

            var mixedMissBattle = new BattleRules(data,
                new Encounter { Kind = "wild", Members = new[] { new MemberSpec { MonId = "kobalt", Level = 14 } } },
                new[] { OwnedMon.New("charmander", 14, 100) }, () => 0.0);
            mixedMissBattle.Player.Base.aim = 10;
            var mixedEvs = mixedMissBattle.Apply(new BattleAction("ability", "flare_burst"));
            Check(mixedEvs.Any(e => e.Type == "miss" && e.Missed), "Mixed missable attack emits explicit miss event when missed");
            Check(mixedMissBattle.Enemy.Hp == mixedMissBattle.Enemy.MaxHp, "Missed mixed attack deals no damage to foe");

            // Bad-luck streak protection prevents 3 consecutive misses
            mixedMissBattle.Apply(new BattleAction("ability", "scratch"));
            var thirdTry = mixedMissBattle.Apply(new BattleAction("ability", "scratch"));
            Check(!thirdTry.Any(e => e.Type == "miss"), "Consecutive miss protection guarantees hit on 3rd attempt");

            // Arena, Boss, and late Captain encounters carry SmartAi; Mega triggers passive surge
            var smartArena = world.Arena(save);
            Check(smartArena.Ai != null && smartArena.Ai.Heal >= 1, "Arena encounter enables SmartAi");
            var megaTest = new BattleRules(data, shieldFoe, new[] { OwnedMon.New("squirtle", 14, 100) }, () => .5);
            megaTest.Player.Shield = 0;
            var megaEvs = megaTest.Apply(new BattleAction("mega"));
            Check(megaTest.Player.Shield == 1 && megaEvs.Any(e => e.Type == "passive" && e.Variant == "shield"),
                "Mega Evolution restores shattered Shell Shield via passive surge");
            Check(megaTest.CatchOddsPct(megaTest.Enemy) >= 0, "CatchOddsPct computes bounded catch percentage");

            foreach (var texPath in new[]
            {
                "Dynamons/Art/general/huds/pane_big_fire", "Dynamons/Art/session/abilities/fx/blast",
                "Dynamons/Art/fonts/white_stroked_big/c65", "Dynamons/Art/shop/products/product_chest"
            })
            {
                Check(Resources.Load<Texture2D>(texPath) != null, "Required texture asset loads: " + texPath);
            }

            DynamonsBuildSettings.Apply();
            Check(PlayerSettings.productName == "Dynamons Next", "Build productName must be Dynamons Next");
            Check(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) == "com.dynamons.game",
                "Android applicationIdentifier must be com.dynamons.game");
            Check(!PlayerSettings.SplashScreen.show, "Unity SplashScreen must be disabled");
            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "Android target must be ARM64");
            Check(PlayerSettings.Android.forceInternetPermission, "Android forceInternetPermission must be enabled");

            // LAN protocol DTO & perspective codec checks
            string encodedCmd = LanCodec.Encode("action", new LanActionCmdDto { seq = 3, actionType = "ability", id = "hellfire" });
            var envelope = LanCodec.Decode<LanWireEnvelope>(encodedCmd);
            Check(envelope != null && envelope.type == "action", "LanWireEnvelope type round-trip");
            var decodedCmd = LanCodec.Decode<LanActionCmdDto>(envelope.payload);
            Check(decodedCmd != null && decodedCmd.seq == 3 && decodedCmd.actionType == "ability" && decodedCmd.id == "hellfire",
                "LanActionCmdDto payload round-trip");
            Check(LanCodec.Decode<LanWireEnvelope>(null) == null && LanCodec.Decode<LanWireEnvelope>("") == null &&
                  LanCodec.Decode<LanWireEnvelope>("{bad-json") == null,
                "LanCodec.Decode returns default on empty or malformed JSON");

            Check(LanCodec.ToLocalSide("host", "host") == "player" &&
                  LanCodec.ToLocalSide("host", "client") == "enemy" &&
                  LanCodec.ToLocalSide("client", "client") == "player" &&
                  LanCodec.ToLocalSide("client", "host") == "enemy" &&
                  LanCodec.ToLocalSide("other", "host") == "other",
                "LanCodec.ToLocalSide maps host/client sides to viewer perspective");

            var sampleVisuals = new[] { new VisualStep { action = "fx-fire", inHypno = "both", at = 120, duration = 450 } };
            var sampleEvent = new BattleEvent
            {
                Type = "ability", Target = "enemy-0", Text = "HELLFIRE", Stat = "atk", Actor = "host-0",
                AbilityId = "hellfire", Side = "player", MonId = "krimson", Variant = "charge", Status = "sick",
                FullPower = true, Backfire = false, Missed = false, Success = true, Ready = true,
                Amount = 25, Hp = 75, ShieldHits = 1, Stacks = 2, BonusPct = 20, Charges = 3, Threshold = 3,
                Visuals = sampleVisuals
            };
            var eventDto = LanCodec.EventToDto(sampleEvent, "host");
            var hostEvent = LanCodec.EventFromDto(eventDto, "host");
            var clientEvent = LanCodec.EventFromDto(eventDto, "client");
            Check(eventDto != null && eventDto.side == "host" &&
                  hostEvent.Side == "player" && clientEvent.Side == "enemy" &&
                  clientEvent.Type == "ability" && clientEvent.Target == "enemy-0" && clientEvent.Text == "HELLFIRE" &&
                  clientEvent.Stat == "atk" && clientEvent.Actor == "host-0" && clientEvent.AbilityId == "hellfire" &&
                  clientEvent.MonId == "krimson" && clientEvent.Variant == "charge" && clientEvent.Status == "sick" &&
                  clientEvent.FullPower && !clientEvent.Backfire && !clientEvent.Missed && clientEvent.Success && clientEvent.Ready &&
                  clientEvent.Amount == 25 && clientEvent.Hp == 75 && clientEvent.ShieldHits == 1 &&
                  clientEvent.Stacks == 2 && clientEvent.BonusPct == 20 && clientEvent.Charges == 3 && clientEvent.Threshold == 3 &&
                  clientEvent.Visuals != null && clientEvent.Visuals.Length == 1 && clientEvent.Visuals[0].action == "fx-fire",
                "LanCodec EventToDto/EventFromDto preserves all BattleEvent fields and flips perspective");

            var neutralEndDto = LanCodec.EventToDto(new BattleEvent { Type = "end", Text = "host", Side = null }, "host");
            var neutralEndFromDto = LanCodec.EventFromDto(neutralEndDto, "client");
            Check(neutralEndDto != null && neutralEndDto.side == null && neutralEndFromDto.Side == null,
                "LanCodec EventToDto preserves neutral null Side on end events");

            // Host-authoritative PvP rules engine checks
            bool threwEmptyRoster = false;
            try
            {
                new PvpBattleRules(data, Array.Empty<LanTeamMemberDto>(),
                    new[] { new LanTeamMemberDto { monId = "krimson", level = 5 } }, "fair20", false, () => 0.1);
            }
            catch (ArgumentException) { threwEmptyRoster = true; }
            Check(threwEmptyRoster, "PvpBattleRules rejects empty roster with ArgumentException");

            // 1. Fair20 vs Real normalization + SaveData immutability
            var pvpSave = SaveData.Create(data, fixtureDay);
            pvpSave.Team.Add(OwnedMon.New("krimson", 1, 12));
            pvpSave.Team[0].Xp = 45;
            pvpSave.Team.Add(OwnedMon.New("leafrog", 18, 37));
            pvpSave.Team[1].Xp = 99;
            pvpSave.Add("heal_spray", 1);
            pvpSave.Add("discatch", 2);
            int initialSprayCount = pvpSave.Count("heal_spray");
            var hostRosterInput = pvpSave.Team.Select(m => new LanTeamMemberDto { monId = m.MonId, level = m.Level }).ToArray();
            var clientRosterInput = new[]
            {
                new LanTeamMemberDto { monId = "charmander", level = 3 },
                new LanTeamMemberDto { monId = "bulbasaur", level = 15 }
            };
            var fairRules = new PvpBattleRules(data, hostRosterInput, clientRosterInput, "fair20", true, () => 0.25);
            Check(fairRules.RuleMode == "fair20" && fairRules.ItemsAllowed &&
                  fairRules.CurrentTurnSide == "host" && fairRules.Phase == "turn" &&
                  fairRules.TurnNumber == 1 && fairRules.Seq == 0 && fairRules.ResolvedTurns == 0,
                "Fair20 PvP initializes expected state and host starter");
            for (int i = 0; i < fairRules.HostTeam.Length; i++)
            {
                var hm = fairRules.HostTeam[i];
                var cm = fairRules.ClientTeam[i];
                Check(hm.Id == "host-" + i && cm.Id == "client-" + i, "PvP combatants use host-i and client-i IDs");
                Check(hm.Level == data.MaxLevel && cm.Level == data.MaxLevel, "Fair20 normalizes all levels to data.MaxLevel");
                Check(hm.Hp == data.battleData.maxHP && hm.MaxHp == data.battleData.maxHP &&
                      cm.Hp == data.battleData.maxHP && cm.MaxHp == data.battleData.maxHP,
                    "Fair20 normalizes all HP to MaxHp");
                Check(hm.Base.atk < 150 && hm.Base.def < 150 && hm.Base.aim < 150 &&
                      cm.Base.atk < 150 && cm.Base.def < 150 && cm.Base.aim < 150,
                    "Fair20 Lv 20 base stats stay under StatCap 150");
                Check(hm.Moves.Length == data.MovesAt(hm.MonId, data.MaxLevel).Length &&
                      cm.Moves.Length == data.MovesAt(cm.MonId, data.MaxLevel).Length,
                    "Fair20 unlocks all moves at MaxLevel");
            }
            var startDto = fairRules.BuildStartMatch();
            Check(startDto.ruleMode == "fair20" && startDto.itemsAllowed && startDto.startingSide == "host" &&
                  startDto.hostTeam.Length == 2 && startDto.clientTeam.Length == 2 &&
                  startDto.hostTeam[0].id == "host-0" && startDto.clientTeam[0].id == "client-0" &&
                  startDto.hostTeam[0].level == data.MaxLevel &&
                  startDto.hostItems.Any(s => s.id == "heal_spray" && s.count == 2) &&
                  startDto.clientItems.Any(s => s.id == "heal_spray" && s.count == 2),
                "BuildStartMatch exports normalized teams, startingSide, and standardized pouches");
            Check(pvpSave.Team[0].Level == 1 && pvpSave.Team[0].Hp == 12 && pvpSave.Team[0].Xp == 45 &&
                  pvpSave.Team[1].Level == 18 && pvpSave.Team[1].Hp == 37 &&
                  pvpSave.Count("heal_spray") == initialSprayCount && pvpSave.Count("discatch") == 2,
                "PvP initialization leaves original SaveData Team and Inventory untouched");

            var realRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 1 } },
                new[] { new LanTeamMemberDto { monId = "leafrog", level = 18 } },
                "real", false, () => 0.8);
            Check(realRules.RuleMode == "real" && !realRules.ItemsAllowed &&
                  realRules.CurrentTurnSide == "client" &&
                  realRules.HostTeam[0].Level == 1 && realRules.ClientTeam[0].Level == 18 &&
                  realRules.HostTeam[0].Hp == data.battleData.maxHP &&
                  realRules.PouchCount("host", "heal_spray") == 0,
                "Real mode preserves clamped levels, selects client starter when rng >= 0.5, and disables items");

            // 2. Strict turn authority & seq validation
            Check(fairRules.SubmitAction("client", new LanActionCmdDto { seq = 0, actionType = "ability", id = fairRules.ClientTeam[0].Moves[0].id }) == null,
                "Out-of-turn action rejected");
            Check(fairRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "ability", id = "hellfire" }) == null,
                "Mismatched seq rejected");
            Check(fairRules.Seq == 0 && fairRules.TurnNumber == 1 && fairRules.CurrentTurnSide == "host",
                "Rejected actions do not advance Seq, TurnNumber, or CurrentTurnSide");

            // 3. Voluntary switch during turn swaps slot, emits switch, ticks cooldowns, and advances turn
            var cdRes = fairRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "hellfire" });
            Check(cdRes != null && cdRes.seq == 0 && cdRes.turnEnded && cdRes.nextTurnSide == "client" &&
                  fairRules.Seq == 1 && fairRules.TurnNumber == 2 && fairRules.ResolvedTurns == 1,
                "Valid host ability resolves and advances seq/turn");
            Check(fairRules.SubmitAction("client", new LanActionCmdDto { seq = 0, actionType = "ability", id = fairRules.ClientTeam[0].Moves[0].id }) == null,
                "Stale seq=0 rejected after turn advanced");
            int hellfireCdAfterAttack = fairRules.HostTeam[0].Cooldowns["hellfire"];
            var clientAtkRes = fairRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "ability", id = "enflame" });
            Check(clientAtkRes != null && clientAtkRes.nextTurnSide == "host" && fairRules.Seq == 2,
                "Client turn resolves cleanly");
            var volSwitchRes = fairRules.SubmitAction("host", new LanActionCmdDto { seq = 2, actionType = "switch", index = 1 });
            Check(volSwitchRes != null && volSwitchRes.activeHostSlot == 1 && volSwitchRes.turnEnded &&
                  volSwitchRes.nextTurnSide == "client" && fairRules.HostSlot == 1 &&
                  fairRules.CurrentTurnSide == "client" && fairRules.TurnNumber == 4 &&
                  volSwitchRes.events.Any(e => e.type == "switch" && e.side == "host" && e.target == "host-1") &&
                  fairRules.HostTeam[0].Cooldowns["hellfire"] == hellfireCdAfterAttack - 1,
                "Voluntary switch swaps slot, emits switch, ticks bench cooldowns, and ends turn");

            // 4. ForcedSwitch on KO & replacement switch
            var koRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 }, new LanTeamMemberDto { monId = "leafrog", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 }, new LanTeamMemberDto { monId = "bulbasaur", level = 20 } },
                "fair20", false, () => 0.25);
            koRules.ClientTeam[0].Hp = 1;
            var koRes = koRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "hellfire" });
            Check(koRes != null && koRes.phase == "forced_switch" && koRes.forcedSwitchSide == "client" && !koRes.turnEnded &&
                  koRules.Phase == "forced_switch" && koRules.ForcedSwitchSide == "client" && koRules.ResolvedTurns == 1,
                "KO with living bench enters forced_switch for defender without ending turn");
            Check(koRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 }) == null,
                "Non-forced side cannot switch during forced_switch");
            Check(koRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "ability", id = "enflame" }) == null,
                "Defender cannot use ability during forced_switch");
            Check(koRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "switch", index = 0 }) == null,
                "Defender cannot switch to fainted slot during forced_switch");
            var repRes = koRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 });
            Check(repRes != null && repRes.phase == "turn" && repRes.forcedSwitchSide == "" &&
                  repRes.nextTurnSide == "client" && repRes.activeClientSlot == 1 && repRes.turnEnded &&
                  koRules.Phase == "turn" && koRules.CurrentTurnSide == "client" && koRules.ClientSlot == 1 &&
                  koRules.TurnNumber == 2,
                "Forced replacement switch returns to turn phase and gives turn to defender");

            // 5a. Self-KO via hypno backfire triggers forced_switch on actorSide and hands turn to defender
            var backfireRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 }, new LanTeamMemberDto { monId = "leafrog", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 }, new LanTeamMemberDto { monId = "bulbasaur", level = 20 } },
                "fair20", false, () => 0.25);
            backfireRules.HostTeam[0].Status = "hypno";
            backfireRules.HostTeam[0].Hp = 1;
            backfireRules.HostTeam[1].Cooldowns["earth_shield"] = 2;
            var bfRes = backfireRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "hellfire" });
            Check(bfRes != null && bfRes.phase == "forced_switch" && bfRes.forcedSwitchSide == "host" && !bfRes.turnEnded &&
                  backfireRules.ClientTeam[0].Hp == backfireRules.ClientTeam[0].MaxHp &&
                  backfireRules.HostTeam[1].Cooldowns["earth_shield"] == 1 &&
                  bfRes.events.Any(e => e.type == "hypno" && e.backfire && e.side == "host") &&
                  bfRes.events.Any(e => e.type == "faint" && e.target == "host-0"),
                "Hypno backfire self-KO triggers forced_switch on actorSide, ticks bench cooldowns, and spares defender");
            var bfSwitch = backfireRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 });
            Check(bfSwitch != null && bfSwitch.phase == "turn" && bfSwitch.nextTurnSide == "client" && bfSwitch.turnEnded &&
                  backfireRules.CurrentTurnSide == "client" && backfireRules.HostSlot == 1,
                "Post-backfire replacement switch hands turn to defender");

            // 5b. Self-KO via sick tick triggers forced_switch on actorSide and hands turn to defender
            var sickRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 }, new LanTeamMemberDto { monId = "leafrog", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 }, new LanTeamMemberDto { monId = "bulbasaur", level = 20 } },
                "fair20", false, () => 0.25);
            sickRules.HostTeam[0].Status = "sick";
            sickRules.HostTeam[0].Hp = 1;
            var sickRes = sickRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "scratch" });
            Check(sickRes != null && sickRes.phase == "forced_switch" && sickRes.forcedSwitchSide == "host" && !sickRes.turnEnded &&
                  sickRes.events.Any(e => e.type == "faint" && e.target == "host-0"),
                "Sick tick self-KO triggers forced_switch on actorSide");
            var sickSwitch = sickRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 });
            Check(sickSwitch != null && sickSwitch.phase == "turn" && sickSwitch.nextTurnSide == "client" && sickSwitch.turnEnded,
                "Post-sick replacement switch hands turn to defender");

            // 5c. Pending sick tick killing attacker AFTER defender forced switch completes
            var dualKoRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 }, new LanTeamMemberDto { monId = "leafrog", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 }, new LanTeamMemberDto { monId = "bulbasaur", level = 20 } },
                "fair20", false, () => 0.25);
            dualKoRules.HostTeam[0].Status = "sick";
            dualKoRules.HostTeam[0].Hp = 1;
            dualKoRules.ClientTeam[0].Hp = 1;
            var dualFirst = dualKoRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "hellfire" });
            Check(dualFirst != null && dualFirst.phase == "forced_switch" && dualFirst.forcedSwitchSide == "client",
                "Attacker KOing defender enters defender forced_switch before attacker sick tick");
            var dualSecond = dualKoRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 });
            Check(dualSecond != null && dualSecond.phase == "forced_switch" && dualSecond.forcedSwitchSide == "host" &&
                  dualSecond.events.Any(e => e.type == "faint" && e.target == "host-0"),
                "Pending sick tick after defender switch triggers actor forced_switch");
            var dualThird = dualKoRules.SubmitAction("host", new LanActionCmdDto { seq = 2, actionType = "switch", index = 1 });
            Check(dualThird != null && dualThird.phase == "turn" && dualThird.nextTurnSide == "client" && dualThird.turnEnded,
                "Both replacements resolve cleanly back to turn phase for defender");

            // 5d. Sleep turn symmetry (isPvp=true): both host and client sleep exact 2 turns
            var hostSleepRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "hyperbug", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 } },
                "fair20", false, () => 0.4);
            hostSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "slumber_hex" });
            Check(hostSleepRules.ClientTeam[0].Status == "sleep" && hostSleepRules.ClientTeam[0].SleepTurns == 2 && !hostSleepRules.ClientTeam[0].SleepFresh,
                "Host sleeping Client sets SleepTurns=2 and SleepFresh=false in PvP");
            var cSleep1 = hostSleepRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "ability", id = "scratch" });
            Check(cSleep1.events.Any(e => e.type == "sleep") && hostSleepRules.ClientTeam[0].Status == "sleep" && hostSleepRules.ClientTeam[0].SleepTurns == 1,
                "Client misses turn 1 of sleep and ticks SleepTurns 2->1");
            hostSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 2, actionType = "ability", id = "gloom_veil" });
            var cSleep2 = hostSleepRules.SubmitAction("client", new LanActionCmdDto { seq = 3, actionType = "ability", id = "scratch" });
            Check(cSleep2.events.Any(e => e.type == "sleep") && cSleep2.events.Any(e => e.type == "wake") &&
                  hostSleepRules.ClientTeam[0].Status == null && hostSleepRules.ClientTeam[0].SleepTurns == 0,
                "Client misses turn 2 of sleep and wakes up at end of 2nd slept turn");
            hostSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 4, actionType = "ability", id = "shadow_claw" });
            var cAwake = hostSleepRules.SubmitAction("client", new LanActionCmdDto { seq = 5, actionType = "ability", id = "scratch" });
            Check(cAwake != null && !cAwake.events.Any(e => e.type == "sleep") && cAwake.events.Any(e => e.type == "damage"),
                "Client acts normally after waking from 2-turn sleep");

            var clientSleepRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "hyperbug", level = 20 } },
                "fair20", false, () => 0.6);
            clientSleepRules.SubmitAction("client", new LanActionCmdDto { seq = 0, actionType = "ability", id = "slumber_hex" });
            Check(clientSleepRules.HostTeam[0].Status == "sleep" && clientSleepRules.HostTeam[0].SleepTurns == 2 && !clientSleepRules.HostTeam[0].SleepFresh,
                "Client sleeping Host symmetrically sets SleepTurns=2 and SleepFresh=false in PvP");
            var hSleep1 = clientSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "ability", id = "scratch" });
            Check(hSleep1.events.Any(e => e.type == "sleep") && clientSleepRules.HostTeam[0].Status == "sleep" && clientSleepRules.HostTeam[0].SleepTurns == 1,
                "Host misses turn 1 of sleep symmetrically");
            clientSleepRules.SubmitAction("client", new LanActionCmdDto { seq = 2, actionType = "ability", id = "gloom_veil" });
            var hSleep2 = clientSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 3, actionType = "ability", id = "scratch" });
            Check(hSleep2.events.Any(e => e.type == "sleep") && hSleep2.events.Any(e => e.type == "wake") &&
                  clientSleepRules.HostTeam[0].Status == null,
                "Host wakes symmetrically after 2 slept turns");

            // Self-sleep via hypno backfire preserves SleepFresh during same-turn Tick so SleepTurns remains 2
            var bfSleepRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "hyperbug", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 } },
                "fair20", false, () => 0.25);
            bfSleepRules.HostTeam[0].Status = "hypno";
            bfSleepRules.HostTeam[0].Base.aim = 100;
            bfSleepRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "slumber_hex" });
            Check(bfSleepRules.HostTeam[0].Status == "sleep" && bfSleepRules.HostTeam[0].SleepTurns == 2 && !bfSleepRules.HostTeam[0].SleepFresh,
                "Hypno backfire self-sleep preserves 2 full sleep turns after same-turn Tick");

            // 6. Mega once per team, passive surge, preserves turn (turnEnded=false), second Mega rejected
            var megaRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "squirtle", level = 20 }, new LanTeamMemberDto { monId = "charmander", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 } },
                "fair20", false, () => 0.25);
            megaRules.HostTeam[0].Shield = 0;
            Check(megaRules.CanMega("host") && megaRules.CanMega("client"), "Both sides start Mega-eligible");
            var megaRes = megaRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "mega" });
            Check(megaRes != null && !megaRes.turnEnded && megaRes.nextTurnSide == "host" && megaRes.phase == "turn" &&
                  megaRes.events.Any(e => e.type == "mega" && e.side == "host") &&
                  megaRes.events.Any(e => e.type == "passive" && e.variant == "shield") &&
                  megaRules.HostTeam[0].Mega && megaRules.HostTeam[0].Shield == 1 && !megaRules.CanMega("host"),
                "Mega restores Shield via passive surge, keeps turn (turnEnded=false), and disables team Mega");
            Check(megaRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "mega" }) == null,
                "Second Mega on same mon rejected");
            megaRules.SubmitAction("host", new LanActionCmdDto { seq = 1, actionType = "switch", index = 1 });
            megaRules.SubmitAction("client", new LanActionCmdDto { seq = 2, actionType = "ability", id = "scratch" });
            Check(!megaRules.CanMega("host") && megaRules.SubmitAction("host", new LanActionCmdDto { seq = 3, actionType = "mega" }) == null,
                "Mega remains locked across the entire team after switching");

            // 7. Catch forbidden; itemsAllowed=false vs isolated PvP pouch
            Check(megaRules.SubmitAction("host", new LanActionCmdDto { seq = 3, actionType = "catch" }) == null,
                "Catch is forbidden in PvP");
            Check(megaRules.SubmitAction("host", new LanActionCmdDto { seq = 3, actionType = "item", id = "heal_spray" }) == null,
                "Item is rejected when itemsAllowed=false");

            var itemRules = new PvpBattleRules(data, hostRosterInput, clientRosterInput, "fair20", true, () => 0.25);
            Check(itemRules.PouchCount("host", "heal_spray") == 2 && itemRules.PouchCount("host", "antidote") == 1 &&
                  itemRules.PouchCount("host", "atk_pepper") == 1 && itemRules.PouchCount("host", "def_berry") == 1 &&
                  itemRules.PouchCount("host", "discatch") == 0,
                "Standardized PvP pouch initialized cleanly when itemsAllowed=true");
            Check(itemRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "catch" }) == null,
                "Catch remains forbidden even when itemsAllowed=true");
            Check(itemRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "item", id = "discatch" }) == null,
                "Discatch item is rejected even when itemsAllowed=true");
            itemRules.HostTeam[0].Hp = 20;
            var healRes = itemRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "item", id = "heal_spray" });
            Check(healRes != null && healRes.turnEnded && healRes.nextTurnSide == "client" &&
                  itemRules.HostTeam[0].Hp == 70 && itemRules.PouchCount("host", "heal_spray") == 1 &&
                  itemRules.PouchCount("client", "heal_spray") == 2 &&
                  pvpSave.Count("heal_spray") == initialSprayCount,
                "PvP heal_spray heals 50 HP from isolated pouch without mutating SaveData.Inventory");
            itemRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "ability", id = "enflame" });
            itemRules.SubmitAction("host", new LanActionCmdDto { seq = 2, actionType = "item", id = "heal_spray" });
            itemRules.SubmitAction("client", new LanActionCmdDto { seq = 3, actionType = "ability", id = "heatwave" });
            Check(itemRules.PouchCount("host", "heal_spray") == 0 &&
                  itemRules.SubmitAction("host", new LanActionCmdDto { seq = 4, actionType = "item", id = "heal_spray" }) == null,
                "Depleted PvP pouch item cannot be reused");
            itemRules.HostTeam[0].Status = "sleep";
            var antidoteRes = itemRules.SubmitAction("host", new LanActionCmdDto { seq = 4, actionType = "item", id = "antidote" });
            Check(antidoteRes != null && itemRules.HostTeam[0].Status == null && itemRules.PouchCount("host", "antidote") == 0,
                "PvP antidote clears status and consumes 1 pouch charge");

            // 8. ResolveTimeoutFallback in turn (ability & cooldown-exhausted switch) and forced_switch
            var timeoutRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 }, new LanTeamMemberDto { monId = "leafrog", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 }, new LanTeamMemberDto { monId = "bulbasaur", level = 20 } },
                "fair20", false, () => 0.25);
            var toTurn = timeoutRules.ResolveTimeoutFallback();
            Check(toTurn != null && toTurn.seq == 0 && toTurn.actorSide == "host" && toTurn.nextTurnSide == "client" && timeoutRules.Seq == 1,
                "ResolveTimeoutFallback executes ready ability in turn phase and advances Seq");
            foreach (var mv in timeoutRules.ClientTeam[0].Moves) timeoutRules.ClientTeam[0].Cooldowns[mv.id] = 2;
            var toSwitchTurn = timeoutRules.ResolveTimeoutFallback();
            Check(toSwitchTurn != null && toSwitchTurn.actorSide == "client" && toSwitchTurn.activeClientSlot == 1 &&
                  toSwitchTurn.nextTurnSide == "host" && timeoutRules.ClientSlot == 1,
                "ResolveTimeoutFallback falls back to alive bench switch when all moves on cooldown");
            timeoutRules.ClientTeam[1].Hp = 1;
            timeoutRules.SubmitAction("host", new LanActionCmdDto { seq = 2, actionType = "ability", id = "hellfire" });
            Check(timeoutRules.Phase == "forced_switch" && timeoutRules.ForcedSwitchSide == "client",
                "Setup forced_switch for timeout fallback test");
            var toForced = timeoutRules.ResolveTimeoutFallback();
            Check(toForced != null && toForced.phase == "turn" && toForced.forcedSwitchSide == "" &&
                  toForced.activeClientSlot == 0 && toForced.nextTurnSide == "client" &&
                  timeoutRules.Phase == "turn" && timeoutRules.ClientSlot == 0,
                "ResolveTimeoutFallback picks first alive bench mon in forced_switch and exits to turn");

            // 9. Forfeit & Turn-1 KO ResolvedTurns anti-farm checks
            var forfeitRules = new PvpBattleRules(data, hostRosterInput, clientRosterInput, "fair20", false, () => 0.25);
            var ffRes = forfeitRules.Forfeit("host");
            Check(ffRes != null && ffRes.phase == "ended" && ffRes.outcome == "client" &&
                  forfeitRules.Phase == "ended" && forfeitRules.Outcome == "client" && forfeitRules.ResolvedTurns == 0 &&
                  ffRes.events.Any(e => e.type == "escape" && e.side == "host") &&
                  ffRes.events.Any(e => e.type == "end" && e.text == "client" && e.side == null),
                "Forfeit ends match with opponent outcome, escape event, neutral end event, and 0 ResolvedTurns");
            Check(forfeitRules.SubmitAction("client", new LanActionCmdDto { seq = 1, actionType = "ability", id = "scratch" }) == null &&
                  forfeitRules.ResolveTimeoutFallback() == null && forfeitRules.Forfeit("client") == null,
                "Ended match rejects further SubmitAction, ResolveTimeoutFallback, and Forfeit");

            var oneHitKoRules = new PvpBattleRules(data,
                new[] { new LanTeamMemberDto { monId = "krimson", level = 20 } },
                new[] { new LanTeamMemberDto { monId = "charmander", level = 20 } },
                "fair20", false, () => 0.25);
            oneHitKoRules.ClientTeam[0].Hp = 1;
            var winRes = oneHitKoRules.SubmitAction("host", new LanActionCmdDto { seq = 0, actionType = "ability", id = "hellfire" });
            Check(winRes != null && winRes.phase == "ended" && winRes.outcome == "host" &&
                  oneHitKoRules.Phase == "ended" && oneHitKoRules.Outcome == "host" && oneHitKoRules.ResolvedTurns == 1,
                "1v1 Turn-1 KO ends match with Outcome==host and ResolvedTurns==1");

            // Static / non-blocking LAN transport & codec sanity checks
            var beaconJson = JsonUtility.ToJson(new LanBeaconDto
            {
                protocolVersion = LanConst.ProtocolVersion,
                roomId = "room-1",
                hostName = "HOST",
                ruleMode = "fair20",
                itemsAllowed = true,
                tcpPort = LanConst.BattlePortBase,
                teamCount = 3
            });
            var decodedBeacon = LanCodec.Decode<LanBeaconDto>(beaconJson);
            Check(decodedBeacon != null && decodedBeacon.protocolVersion == LanConst.ProtocolVersion &&
                  decodedBeacon.roomId == "room-1" && decodedBeacon.hostName == "HOST" &&
                  decodedBeacon.ruleMode == "fair20" && decodedBeacon.itemsAllowed &&
                  decodedBeacon.tcpPort == LanConst.BattlePortBase && decodedBeacon.teamCount == 3,
                "LanBeaconDto round-trips cleanly via JsonUtility/LanCodec");

            string pingWire = LanCodec.Encode("ping", new LanPingDto { ts = 424242L });
            var pingEnv = LanCodec.Decode<LanWireEnvelope>(pingWire);
            var pingPayload = pingEnv != null ? LanCodec.Decode<LanPingDto>(pingEnv.payload) : null;
            Check(pingEnv != null && pingEnv.type == "ping" && pingPayload != null && pingPayload.ts == 424242L,
                "LanPingDto envelope round-trips cleanly");

            var localIps = LanTransport.LocalIpv4Addresses();
            Check(localIps != null &&
                  localIps.All(ip => !string.IsNullOrEmpty(ip) && ip != "127.0.0.1" && !ip.StartsWith("169.254.", StringComparison.Ordinal)),
                "LanTransport.LocalIpv4Addresses filters loopback and link-local addresses");
            Check(LanConst.DiscoveryMulticastIp == "239.255.41.75", "LanConst.DiscoveryMulticastIp must match expected LAN multicast group");
            Check(Equals(LanTransport.PrefixLengthToMask(24), System.Net.IPAddress.Parse("255.255.255.0")) &&
                  Equals(LanTransport.PrefixLengthToMask(16), System.Net.IPAddress.Parse("255.255.0.0")),
                "LanTransport.PrefixLengthToMask computes exact IPv4 netmasks");
            Check(Equals(LanTransport.ComputeBroadcastIp(System.Net.IPAddress.Parse("192.168.1.45"), System.Net.IPAddress.Parse("255.255.255.0")),
                      System.Net.IPAddress.Parse("192.168.1.255")) &&
                  Equals(LanTransport.ComputeBroadcastIp(System.Net.IPAddress.Parse("10.0.5.12"), System.Net.IPAddress.Parse("255.255.0.0")),
                      System.Net.IPAddress.Parse("10.0.255.255")) &&
                  Equals(LanTransport.ComputeBroadcastIp(System.Net.IPAddress.Parse("192.168.50.10"), null),
                      System.Net.IPAddress.Parse("192.168.50.255")),
                "LanTransport.ComputeBroadcastIp handles directed masks and /24 fallback");
            Check(LanTransport.IsVirtualInterfaceName("docker0") &&
                  LanTransport.IsVirtualInterfaceName("virbr0") &&
                  !LanTransport.IsVirtualInterfaceName("wlan0") &&
                  !LanTransport.IsVirtualInterfaceName("wlp3s0"),
                "LanTransport.IsVirtualInterfaceName distinguishes virtual bridges from physical Wi-Fi");

            var dummyRoom = new LanDiscoveredRoom
            {
                Key = "192.168.1.2:41976",
                HostIp = "192.168.1.2",
                TcpPort = LanConst.BattlePortBase,
                HostName = "HOST",
                RuleMode = "fair20",
                ItemsAllowed = false,
                TeamCount = 2,
                LastSeenUtc = DateTime.UtcNow
            };
            var dummyNetMsg = new LanNetMessage { Type = "connected", JsonPayload = "{}", Reason = string.Empty };
            Check(dummyRoom.Key == "192.168.1.2:41976" && dummyNetMsg.Type == "connected",
                "LanDiscoveredRoom and LanNetMessage surface expected fields");

            using (var idleTransport = new LanTransport())
            {
                Check(!idleTransport.IsHost && !idleTransport.IsConnected && idleTransport.BoundPort == 0,
                    "Fresh LanTransport starts disconnected with BoundPort 0");
                var initialRooms = idleTransport.GetRooms();
                var initialMsgs = idleTransport.DrainMainThreadMessages(0f);
                Check(initialRooms != null && initialRooms.Count == 0 && initialMsgs != null && initialMsgs.Count == 0,
                    "Idle LanTransport GetRooms and DrainMainThreadMessages return empty non-null lists");
                idleTransport.Send("ping", new LanPingDto { ts = 1L });
                idleTransport.UpdateBeacon(decodedBeacon);
                idleTransport.Stop();
                idleTransport.Stop();
            }

            Debug.Log("Dynamons rules & parity checks passed.");
        }
    }
}
