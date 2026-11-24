using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    internal sealed class CombatantView
    {
        public string Id, MonId, Status;
        public int Level, Hp, MaxHp, Shield, VoidStacks, PowerStacks, Charges, AtkMod, DefMod, AimMod;
        public bool Rage, Mega;
        public Passive Passive;
        public MoveData[] Moves;

        public static CombatantView From(Combatant c) => new CombatantView
        {
            Id = c.Id, MonId = c.MonId, Level = c.Level, Hp = c.Hp, MaxHp = c.MaxHp,
            Status = c.Status, Rage = c.Rage, Mega = c.Mega, Shield = c.Shield,
            VoidStacks = c.VoidStacks, PowerStacks = c.PowerStacks, Charges = c.Charges,
            AtkMod = c.Mods.atk, DefMod = c.Mods.def, AimMod = c.Mods.aim,
            Passive = GameData.HasPassive(c.Passive) ? c.Passive : null, Moves = c.Moves,
        };
    }

    public sealed partial class GameRoot
    {
        private sealed class FeedbackEntry
        {
            public BattleEvent Source;
            public float Elapsed, Duration;
        }

        private BattleRules battle;
        private bool battleActive, battleMoreOpen, battleForcedSwap, battleWaiting, battleEventStarted;
        private Reward[] pendingBattleLoot = Array.Empty<Reward>();
        private bool pendingBattleSaved = true;

        private List<CombatantView> viewPlayer = new List<CombatantView>();
        private List<CombatantView> viewEnemy = new List<CombatantView>();
        private int viewActivePlayer, viewActiveEnemy;
        private readonly Dictionary<string, float> hpTrail = new Dictionary<string, float>();
        private readonly Dictionary<string, int> battleXpEarned = new Dictionary<string, int>();
        private readonly List<FeedbackEntry> feedbacks = new List<FeedbackEntry>();

        private BattleEvent[] battleEvents = Array.Empty<BattleEvent>();
        private int battleEventIndex;
        private float battleEventTimer, battleTimeMs, battleLogTimer, battleTurnPauseMs;
        private string battleLog = string.Empty;
        private float trayStartX = GameUi.Width - 4f * 59f;

        private RectTransform arenaLayer, fxSpritesLayer, hpHudLayer, veilLayer, topHudLayer, trayBgLayer, controlsLayer, itemsModalLayer;
        private VectorCanvas fxBgVec, arenaAuraVec, fxFgVec, rainbowBorderVec;
        private readonly List<(float x, float y, float w, float h, float phase, bool strongGlow)> rainbowTargets =
            new List<(float, float, float, float, float, bool)>();
        private string modalSelectedItemId;
        private int modalItemPage, modalItemFilter;
        private bool modalPickingStoneTarget, enemyTurnPauseDone;
        private string modalStoneId, passiveInspectSide;

        private static BattleBox PositionBox(string side) => side == "enemy"
            ? new BattleBox(174f, 151f, 132f, 150f, 240f, 301f)
            : new BattleBox(12f, 276f, 136f, 150f, 80f, 426f);

        private void ResetBattleState()
        {
            battleActive = false;
            battleWaiting = false;
            battleEventStarted = false;
            battleMoreOpen = false;
            battleForcedSwap = false;
            battleTurnPauseMs = 0f;
            enemyTurnPauseDone = false;
            modalItemFilter = 0;
            passiveInspectSide = null;
            battleEvents = Array.Empty<BattleEvent>();
            feedbacks.Clear();
            rainbowTargets.Clear();
            arenaLayer = null;
            fxSpritesLayer = null;
            hpHudLayer = null;
            veilLayer = null;
            topHudLayer = null;
            trayBgLayer = null;
            controlsLayer = null;
            itemsModalLayer = null;
            fxBgVec = null;
            arenaAuraVec = null;
            fxFgVec = null;
            rainbowBorderVec = null;
        }

        private void BeginBattle(Encounter encounter)
        {
            var startDialog = encounter.Dialogs?.dialog_start;
            if (!string.IsNullOrEmpty(startDialog) && !save.SeenDialogs.Contains(startDialog) && data.Dialog(startDialog)?.content?.Length > 0)
            { Dialogue(startDialog, () => BeginBattle(encounter)); return; }

            battle = flow.StartBattle(encounter, DateTime.Today);
            if (battle == null)
            { ShowMap(); Note(flow.LivingParty().Length == 0 ? "Heal your party first." : "This battle is not available."); return; }
            if (encounter.Kind == "starter_event" && !Persist())
            { flow.CancelEntry(battle); battle = null; ShowMap(); Note("Could not save the ticket. Please try again."); return; }

            BeginPage(null);
            battleActive = true;
            battleTimeMs = 0f;
            battleLog = string.Empty;
            battleLogTimer = 0f;
            hpTrail.Clear();
            battleXpEarned.Clear();
            SnapshotBattleView();

            string intro = encounter.Kind == "boss" ? "music_battle_boss_start" : encounter.Kind == "arena" ? "music_arena_start" : "music_battle_start";
            ui.Sound(intro, save);
            Track(encounter.Kind == "arena" ? "music_arena_loop" : "music_battle_loop");

            ui.ArtFit(page, "session/bgs/" + (string.IsNullOrEmpty(encounter.Background) ? "woods" : encounter.Background), 0f, 0f, GameUi.Width, GameUi.Height);
            fxBgVec = VectorCanvas.Create("FxBgVec", page, 0f, 0f, GameUi.Width, GameUi.Height);
            arenaLayer = GameUi.Box("ArenaSprites", page, 0f, 0f, GameUi.Width, GameUi.Height);
            arenaAuraVec = VectorCanvas.Create("ArenaAuraVec", page, 0f, 0f, GameUi.Width, GameUi.Height);
            fxSpritesLayer = GameUi.Box("FxSprites", page, 0f, 0f, GameUi.Width, GameUi.Height);
            fxFgVec = VectorCanvas.Create("FxFgVec", page, 0f, 0f, GameUi.Width, GameUi.Height);
            hpHudLayer = GameUi.Box("BattleHpHud", page, 0f, 0f, GameUi.Width, GameUi.Height);
            veilLayer = GameUi.Box("BattleVeil", page, 0f, 0f, GameUi.Width, GameUi.Height);
            topHudLayer = GameUi.Box("BattleTopHud", page, 0f, 0f, GameUi.Width, GameUi.Height);
            trayBgLayer = GameUi.Box("BattleTrayBg", page, 0f, 0f, GameUi.Width, GameUi.Height);
            controlsLayer = GameUi.Box("BattleControls", page, 0f, 0f, GameUi.Width, GameUi.Height);
            rainbowBorderVec = VectorCanvas.Create("BattleRainbowVec", page, 0f, 0f, GameUi.Width, GameUi.Height);
            itemsModalLayer = GameUi.Box("BattleItemsModal", page, 0f, 0f, GameUi.Width, GameUi.Height);

            RebuildBattleControls();
            RenderDynamicBattleFrame();
        }

        private void SnapshotBattleView()
        {
            viewPlayer = battle.Party.Select(CombatantView.From).ToList();
            viewEnemy = battle.Opponents.Select(CombatantView.From).ToList();
            viewActivePlayer = battle.PlayerSlot;
            viewActiveEnemy = battle.EnemySlot;
            foreach (var mon in viewPlayer.Concat(viewEnemy))
                hpTrail[mon.Id] = mon.MaxHp > 0 ? Mathf.Clamp01(mon.Hp / (float)mon.MaxHp) : 0f;
        }

        private void UpdateBattleLoop()
        {
            if (!battleActive || battle == null) return;
            float scaledDtMs = Time.unscaledDeltaTime * 1000f * Mathf.Clamp(save.Speed, 1, 3);
            battleTimeMs += scaledDtMs;

            if (battleLogTimer > 0f)
            {
                battleLogTimer = Mathf.Max(0f, battleLogTimer - scaledDtMs);
                if (battleLogTimer <= 0f) battleLog = string.Empty;
            }

            foreach (var mon in viewPlayer.Concat(viewEnemy))
            {
                float target = mon.MaxHp > 0 ? Mathf.Clamp01(mon.Hp / (float)mon.MaxHp) : 0f;
                float cur = hpTrail.TryGetValue(mon.Id, out float t) ? t : target;
                hpTrail[mon.Id] = cur > target ? Mathf.Max(target, cur - scaledDtMs * 0.00075f) : target;
            }

            for (int i = feedbacks.Count - 1; i >= 0; i--)
            {
                feedbacks[i].Elapsed += scaledDtMs;
                if (feedbacks[i].Elapsed >= feedbacks[i].Duration) feedbacks.RemoveAt(i);
            }

            if (battleWaiting)
            {
                battleEventTimer += scaledDtMs;
                while (battleWaiting)
                {
                    if (battleEventIndex >= battleEvents.Length)
                    {
                        battleWaiting = false;
                        battleEventStarted = false;
                        battleTurnPauseMs = 0f;
                        enemyTurnPauseDone = false;
                        battleEvents = Array.Empty<BattleEvent>();
                        SnapshotBattleView();
                        if (battle.Outcome != null)
                        {
                            battleActive = false;
                            ShowResult(pendingBattleLoot, pendingBattleSaved);
                            return;
                        }
                        if (battle.NeedsSwitch) { battleForcedSwap = true; battleMoreOpen = true; }
                        RebuildBattleControls();
                        if (!pendingBattleSaved) { pendingBattleSaved = true; Note("Could not save progress. Retry after freeing storage."); }
                        break;
                    }
                    var ev = battleEvents[battleEventIndex];
                    if (!battleEventStarted && !enemyTurnPauseDone && battleEventIndex > 0 &&
                        ev.Side == "enemy" && (ev.Type == "ability" || ev.Type == "mega" || ev.Type == "sleep"))
                    {
                        enemyTurnPauseDone = true;
                        battleTurnPauseMs = 320f;
                    }
                    if (battleTurnPauseMs > 0f)
                    {
                        float consume = Mathf.Min(battleTurnPauseMs, battleEventTimer);
                        battleTurnPauseMs -= consume;
                        battleEventTimer -= consume;
                        if (battleTurnPauseMs > 0f) break;
                    }
                    if (!battleEventStarted) { battleEventStarted = true; BeginBattleEvent(ev); }
                    float dur = BattleEventDuration(ev);
                    if (battleEventTimer < dur) break;
                    battleEventTimer -= dur;
                    battleEventIndex++;
                    battleEventStarted = false;
                    HandleBattleEvent(ev);
                }
            }

            RenderDynamicBattleFrame();
        }

        private float BattleEventDuration(BattleEvent ev)
        {
            switch (ev.Type)
            {
                case "ability":
                {
                    var steps = ev.Visuals ?? Array.Empty<VisualStep>();
                    float rawTotal = steps.Length > 0 ? steps.Max(s => s.at + s.duration) : 600f;
                    return Mathf.Clamp(Mathf.Round(rawTotal * 0.65f), 480f, 950f);
                }
                case "damage": return 300f;
                case "miss": return 760f;
                case "hypno": return 560f;
                case "faint": return 320f;
                case "immune": return 680f;
                case "stat": return 450f;
                case "heal": return 460f;
                case "status": return 460f;
                case "rage": return 460f;
                case "passive": return ev.Ready ? 360f : 180f;
                case "sleep": return 620f;
                case "wake": return 450f;
                case "catch": return 900f;
                case "mega": return 950f;
                case "end": return 300f;
                default: return 220f;
            }
        }

        private CombatantView FindViewMon(string targetId) =>
            viewPlayer.FirstOrDefault(m => m.Id == targetId) ?? viewEnemy.FirstOrDefault(m => m.Id == targetId);

        private void PushFeedback(BattleEvent ev, float duration)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Target)) return;
            bool drawable = ev.Type == "critical" || ev.Type == "strong" || ev.Type == "weak" || ev.Type == "hypno" ||
                            ev.Type == "rage" || ev.Type == "miss" || ev.Type == "sleep" ||
                            (ev.Type == "status" && ev.Status == "sick") || ev.Type == "immune" ||
                            ev.Type == "damage" || ev.Type == "heal" || (ev.Type == "stat" && !string.IsNullOrEmpty(ev.Stat));
            if (!drawable || feedbacks.Any(f => ReferenceEquals(f.Source, ev))) return;
            feedbacks.Add(new FeedbackEntry { Source = ev, Elapsed = 0f, Duration = duration });
        }

        private void LogBattleBanner(string text) { battleLog = text ?? string.Empty; battleLogTimer = 950f; }

        private void BeginBattleEvent(BattleEvent ev)
        {
            if (ev.Type == "ability")
            {
                var steps = ev.Visuals ?? Array.Empty<VisualStep>();
                var sfxStep = steps.FirstOrDefault(s => s.action.StartsWith("sfx-", StringComparison.Ordinal));
                if (sfxStep != null) ui.Sound("attack_" + sfxStep.action.Substring(4), save);
                else if (steps.Any(s => s.action.StartsWith("heal_spray", StringComparison.Ordinal) || s.action.StartsWith("antidote", StringComparison.Ordinal))) ui.Sound("spray_powerup", save);
                else if (steps.Any(s => s.action.StartsWith("atk_pepper", StringComparison.Ordinal) || s.action.StartsWith("def_berry", StringComparison.Ordinal) || s.action.StartsWith("aim_shroom", StringComparison.Ordinal))) ui.Sound("eating_powerup", save);
                else ui.Sound(steps.Any(s => s.action.StartsWith("can_", StringComparison.Ordinal)) ? "canned_powerup" : "attack_scratch", save);
                if (!string.IsNullOrEmpty(ev.Text)) LogBattleBanner(ev.Text.ToUpperInvariant());
            }
            else if (ev.Type == "miss")
            {
                LogBattleBanner("MISSED!");
                bool playedSfx = battleEventIndex > 0 && battleEvents[battleEventIndex - 1].Type == "ability" && battleEvents[battleEventIndex - 1].Missed;
                if (!playedSfx) ui.Sound("attack_miss", save);
                PushFeedback(ev, 760f);
            }
            else if (ev.Type == "hypno") { LogBattleBanner("HYPNO BACKFIRE!"); ui.Sound("hypno", save); PushFeedback(ev, 560f); }
            else if (ev.Type == "heal") { LogBattleBanner($"HEALED {ev.Amount} HP"); ui.Sound("use_healing_kit", save); PushFeedback(ev, 460f); }
            else if (ev.Type == "antidote") LogBattleBanner("STATUS RESTORED!");
            else if (ev.Type == "rage") { LogBattleBanner("RAGE!"); ui.Sound("attack_enrage", save); PushFeedback(ev, 460f); }
            else if (ev.Type == "status")
            {
                LogBattleBanner(ev.Status == "sick" ? "SICK!" : ev.Status == "sleep" ? "ASLEEP!" : "HYPNO!");
                ui.Sound(ev.Status == "sick" ? "sick" : "hypno", save);
                PushFeedback(ev, 460f);
            }
            else if (ev.Type == "catch") ui.Sound(ev.Success ? "catch_success" : "catch_failure", save);
            else if (ev.Type == "faint") ui.Sound("faint", save);
            else if (ev.Type == "mega") { ui.Sound("level_up_animation", save); LogBattleBanner("MEGA EVOLUTION! 30% POWER UP!"); }
            else if (ev.Type == "immune")
            {
                if (ev.Variant == "shield") { LogBattleBanner("SHELL SHIELD SHATTERED!"); ui.Sound("attack_iceberg", save); }
                else { LogBattleBanner($"VOID IMMUNE! DMG UP {ev.BonusPct}%"); ui.Sound("attack_miss", save); }
                PushFeedback(ev, 680f);
            }
            else if (ev.Type == "critical") { LogBattleBanner("CRITICAL HIT!"); ui.Sound("critical_hit", save); PushFeedback(ev, 560f); }
            else if (ev.Type == "strong") { LogBattleBanner("SUPER EFFECTIVE!"); PushFeedback(ev, 560f); }
            else if (ev.Type == "weak") { LogBattleBanner("WEAK ATTACK!"); PushFeedback(ev, 560f); }
            else if (ev.Type == "stat" && !string.IsNullOrEmpty(ev.Stat))
            {
                LogBattleBanner($"{ev.Stat.ToUpperInvariant()} {(ev.Amount > 0 ? "UP" : "DOWN")} {Math.Abs(ev.Amount)}!");
                ui.Sound(ev.Amount > 0 ? "stat_raised" : "stat_lowered", save);
                PushFeedback(ev, 620f);
            }
            else if (ev.Type == "passive")
            {
                string banner = ev.Variant == "charge" && ev.Ready
                    ? $"{(FindViewMon(ev.Target)?.Passive?.boostedTitle ?? "SPORE SHOT FULL POWER").ToUpperInvariant()} READY!"
                    : ev.Text?.ToUpperInvariant();
                if (!string.IsNullOrEmpty(banner)) LogBattleBanner(banner);
                if (ev.Ready || !string.IsNullOrEmpty(ev.Text)) ui.Sound("stat_raised", save);
            }
            else if (ev.Type == "sleep") { LogBattleBanner("FAST ASLEEP!"); PushFeedback(ev, 620f); }
            else if (ev.Type == "wake") { LogBattleBanner("WOKE UP!"); ui.Sound("stat_raised", save); }
        }

        private void HandleBattleEvent(BattleEvent ev)
        {
            var mon = FindViewMon(ev.Target);
            if (mon != null)
            {
                if (ev.Hp >= 0) mon.Hp = ev.Hp;
                if (ev.Type == "faint") { mon.Hp = 0; mon.Status = null; mon.Rage = false; }
                if (ev.Type == "status") { mon.Status = ev.Status; mon.Rage = false; }
                if (ev.Type == "antidote" || ev.Type == "wake") mon.Status = null;
                if (ev.Type == "immune" && ev.Variant == "void") mon.VoidStacks = ev.Stacks;
                if (ev.Type == "immune" && ev.Variant == "shield") mon.Shield = ev.ShieldHits;
                if (ev.Type == "passive" && ev.Variant == "shield") mon.Shield = ev.ShieldHits;
                if (ev.Type == "passive" && ev.Variant == "void") mon.VoidStacks = ev.Stacks;
                if (ev.Type == "passive" && ev.Variant == "ramp") mon.PowerStacks = ev.Stacks;
                if (ev.Type == "passive" && ev.Variant == "charge") mon.Charges = ev.Charges;
                if (ev.Type == "stat" && !string.IsNullOrEmpty(ev.Stat))
                {
                    if (ev.Stat == "atk") mon.AtkMod += ev.Amount;
                    else if (ev.Stat == "def") mon.DefMod += ev.Amount;
                    else if (ev.Stat == "aim") mon.AimMod += ev.Amount;
                }
                if (ev.Type == "rage") { mon.Rage = true; mon.Status = null; }
                if (ev.Type == "rageSpent") mon.Rage = false;
                if (ev.Type == "mega") mon.Mega = true;
            }
            if (ev.Type == "switch")
            {
                var sideList = ev.Side == "enemy" ? viewEnemy : viewPlayer;
                int idx = sideList.FindIndex(m => m.Id == ev.Target);
                if (idx >= 0) { if (ev.Side == "enemy") viewActiveEnemy = idx; else viewActivePlayer = idx; }
            }
            PushFeedback(ev, ev.Type == "damage" ? 450f : ev.Type == "stat" ? 620f : 560f);
            if (ev.Type == "faint") LogBattleBanner("FAINTED!");
            else if (ev.Type == "catch") LogBattleBanner(ev.Success ? "CAUGHT!" : "IT BROKE FREE!");
            else if (ev.Type == "mega") LogBattleBanner("MEGA POWER UP 30%!");
            else if (ev.Type == "escape") LogBattleBanner("GOT AWAY!");
        }

        private void BattleClick(BattleAction action)
        {
            if (battleWaiting || battle == null || battle.Outcome != null) return;
            var steps = battle.Apply(action, save);
            if (steps.Length == 0) return;

            foreach (var ev in steps)
                if (ev.Type == "xp" && !string.IsNullOrEmpty(ev.Target))
                    battleXpEarned[ev.Target] = (battleXpEarned.TryGetValue(ev.Target, out int cur) ? cur : 0) + ev.Amount;

            battleMoreOpen = false;
            battleForcedSwap = false;
            passiveInspectSide = null;
            GameUi.Clear(itemsModalLayer);

            pendingBattleLoot = battle.Outcome == null ? Array.Empty<Reward>() : flow.CommitBattle(battle, DateTime.Today);
            bool needsSave = action.Type == "item" || action.Type == "catch" || battle.Outcome != null;
            pendingBattleSaved = !needsSave || Persist();

            battleEvents = steps;
            battleEventIndex = 0;
            battleEventTimer = 0f;
            battleTurnPauseMs = 0f;
            enemyTurnPauseDone = false;
            battleEventStarted = false;
            battleWaiting = true;
            RebuildBattleControls();
        }

        private void RebuildBattleControls()
        {
            if (controlsLayer == null || battle == null) return;
            GameUi.Clear(controlsLayer);
            GameUi.Clear(trayBgLayer);
            GameUi.Clear(veilLayer);
            rainbowTargets.Clear();

            ui.HitBox(controlsLayer, 256f, 39f, 58f, 24f, () =>
            {
                save.Speed = save.Speed % 3 + 1;
                Persist();
                ui.Sound("button_click", save);
                RebuildBattleControls();
            });
            HudWidgets.DrawMuteButton(ui, controlsLayer, save, musicTrack, () => Persist(), RebuildBattleControls);

            if (battle.Outcome != null) return;
            var actor = battle.Player;
            bool interactive = !battleWaiting;

            if (battleMoreOpen || battleForcedSwap)
            {
                GameUi.Fill(veilLayer, new Color(0f, 0f, 0f, 0.68f), 0f, 0f, GameUi.Width, GameUi.Height);
                if (battleForcedSwap) fonts.Draw(veilLayer, "white_stroked_small", "CHOOSE NEXT DYNAMON!", GameUi.Width * 0.5f, 134f, 12f, "center");
                int row = 0;
                for (int i = 0; i < battle.Party.Length; i++)
                {
                    if (i == battle.PlayerSlot) continue;
                    int slotIdx = i;
                    var mon = battle.Party[i];
                    float sy = 428f - row * 74f;
                    bool alive = mon.Hp > 0;
                    HudWidgets.DrawMonPane(ui, fonts, data, veilLayer, MonHudView.FromCombatant(mon), "thumb", 8f, sy, alpha: alive ? 1f : 0.3f);
                    if (alive && interactive)
                    {
                        ui.HitBox(controlsLayer, 8f, sy, 120f, 65f, () =>
                        {
                            ui.Sound("button_click", save);
                            BattleClick(new BattleAction("switch", index: slotIdx));
                        });
                    }
                    row++;
                }

                if (!battleForcedSwap)
                {
                    ui.ArtButton(controlsLayer, "session/ui/btn_back", 5f, GameUi.Height - 64f, 60f, 60f, () =>
                    {
                        if (battleWaiting) return;
                        ui.Sound("button_click", save);
                        battleMoreOpen = false;
                        RebuildBattleControls();
                    }, interactive);
                }

                var misc = new List<(string img, bool enabled, bool isCatch, Action act)>
                {
                    ("session/ui/btn_escape", interactive, false, () => BattleClick(new BattleAction("escape"))),
                    ("map/btn_items", interactive && !battleForcedSwap, false, OpenBattleItemPicker),
                };
                if (battle.Encounter.CanCapture)
                {
                    bool canCatch = !battleForcedSwap && battle.Enemy.Hp > 0 && save.Count("discatch") > 0;
                    misc.Add((canCatch ? "session/ui/btn_catch" : "session/ui/btn_catch_off", interactive && canCatch, true, () => BattleClick(new BattleAction("catch"))));
                }
                const float miscStep = 59f;
                trayStartX = GameUi.Width - misc.Count * miscStep;
                ui.Art(trayBgLayer, "general/hud_bg", trayStartX - 13f, GameUi.Height - 40f, 250f, 40f);
                for (int i = 0; i < misc.Count; i++)
                {
                    var m = misc[i];
                    float bx = trayStartX + i * miscStep, by = GameUi.Height - 64f;
                    ui.ArtButton(controlsLayer, m.img, bx, by, 55f, 60f, m.act, m.enabled);
                    if (m.isCatch)
                    {
                        if (m.enabled)
                        {
                            var oddsVec = VectorCanvas.Create("CatchOddsVec", controlsLayer, bx - 2f, by - 4f, 34f, 15f);
                            oddsVec.FillRoundRect(0f, 0f, 34f, 15f, 5f, GameUi.HexColor("#0d2c45", 0.95f));
                            oddsVec.StrokeRoundRect(0.5f, 0.5f, 33f, 14f, 4.5f, GameUi.HexColor("#57d4ff", 0.95f), 1.2f);
                            oddsVec.Commit();
                            fonts.Draw(controlsLayer, "white_stroked_small", $"{battle.CatchOddsPct(battle.Enemy)}%", bx + 15f, by - 1f, 7f, "center", GameUi.HexColor("#94ebff"));
                        }
                        ui.Art(controlsLayer, "general/badge_red", bx + 34f, by - 4f, 22f, 22f, true);
                        fonts.Draw(controlsLayer, "white_stroked_small", save.Count("discatch").ToString(), bx + 45f, by + 1f, 8f, "center");
                    }
                }
                return;
            }

            bool megaReady = battle.CanMega;
            bool megaActive = actor.Mega;
            ui.ArtButton(controlsLayer, "session/ui/btn_mega", 9f, 442f, 52f, 52f,
                () => BattleClick(new BattleAction("mega")), interactive && megaReady, megaActive ? 1f : (interactive && megaReady ? 1f : 0.45f));
            if (megaActive || (megaReady && interactive))
                rainbowTargets.Add((8f, 441f, 54f, 54f, 0f, megaActive));

            if (interactive)
            {
                var curFoe = viewActiveEnemy < viewEnemy.Count ? viewEnemy[viewActiveEnemy] : null;
                var curPlayer = viewActivePlayer < viewPlayer.Count ? viewPlayer[viewActivePlayer] : null;
                if (GameData.HasPassive(curFoe?.Passive))
                    ui.HitBox(controlsLayer, 158f, 68f, 112f, 22f, () => OpenPassiveInspect("enemy"));
                if (GameData.HasPassive(curPlayer?.Passive))
                    ui.HitBox(controlsLayer, 214f, 419f, 101f, 20f, () => OpenPassiveInspect("player"));
            }

            var moves = (actor.Moves ?? Array.Empty<MoveData>()).Take(5).ToArray();
            bool compact = moves.Length >= 5;
            float btnSize = compact ? 48f : 55f, step = compact ? 51f : 59f;
            float btnY = compact ? GameUi.Height - 58f : GameUi.Height - 61f;
            trayStartX = GameUi.Width - moves.Length * step;
            ui.Art(trayBgLayer, "general/hud_bg", trayStartX - 13f, GameUi.Height - 40f, 250f, 40f);

            ui.ArtButton(controlsLayer, "session/ui/btn_more", compact ? 4f : 5f, compact ? GameUi.Height - 62f : GameUi.Height - 64f,
                compact ? 56f : 60f, compact ? 56f : 60f, () =>
                {
                    if (battleWaiting) return;
                    ui.Sound("button_click", save);
                    battleMoreOpen = true;
                    RebuildBattleControls();
                }, interactive);

            var viewP = viewActivePlayer < viewPlayer.Count ? viewPlayer[viewActivePlayer] : null;
            for (int i = 0; i < moves.Length; i++)
            {
                var move = moves[i];
                float bx = trayStartX + i * step;
                bool ready = actor.Ready(move);
                int cd = actor.Cooldowns.TryGetValue(move.id, out int turns) ? turns : 0;
                bool sporeReady = actor.Passive?.type == "charge" && move.id == actor.Passive.abilityId &&
                                  actor.Charges >= Math.Max(1, actor.Passive.threshold);
                ui.ArtButton(controlsLayer, GameUi.Move(move.id), bx, btnY, btnSize, btnSize,
                    () => BattleClick(new BattleAction("ability", move.id)), interactive && ready, 1f);
                if (((viewP != null && viewP.Mega) || sporeReady) && ready && interactive)
                    rainbowTargets.Add((bx + 1f, btnY + 1f, btnSize - 2f, btnSize - 2f, i * 220f, false));
                if (!ready && cd > 0)
                {
                    ui.Art(controlsLayer, "session/ui/icon_off", bx, btnY, btnSize, btnSize, true, 0.85f);
                    float maxCd = Mathf.Max(1f, move.cooldown + 1f);
                    float ratio = Mathf.Clamp01((maxCd - cd) / maxCd);
                    HudWidgets.DrawClippedBar(ui, controlsLayer, "general/huds/hpbar_small_bg", "general/huds/hpbar_small_fill",
                        bx + 2f, compact ? btnY + btnSize - 14f : btnY + 34f, compact ? btnSize - 4f : 50f, compact ? 11f : 12f, ratio, 0.09f, 0.91f);
                    fonts.Draw(controlsLayer, "white_stroked_small", cd.ToString(), bx + btnSize * 0.5f, btnY + (compact ? 10f : 13f), compact ? 11f : 12f, "center");
                }
                else if (!interactive)
                {
                    ui.Art(controlsLayer, "session/ui/icon_off", bx, btnY, btnSize, btnSize, true, 0.45f);
                }
                if (battle.Enemy != null && move.power > 0 && !string.IsNullOrEmpty(move.effect) && move.effect.Contains("attack"))
                {
                    float tipAlpha = ready && interactive ? 1f : 0.6f;
                    if (GameData.Strong(move.kind, battle.Enemy.Kind))
                        ui.Art(controlsLayer, "general/huds/tip_strong", bx + (btnSize - 42f) * 0.5f, btnY - 12f, 42f, 13f, true, tipAlpha);
                    else if (GameData.Strong(battle.Enemy.Kind, move.kind))
                        ui.Art(controlsLayer, "general/huds/tip_weak", bx + (btnSize - 42f) * 0.5f, btnY - 12f, 42f, 13f, true, tipAlpha);
                }
            }
        }

        private void OpenBattleItemPicker()
        {
            if (battleWaiting || battle.Outcome != null) return;
            ui.Sound("screen_open", save);
            bool blockCans = battle.Player.Status == "sleep";
            OpenItemsOverlay(true, battle.Encounter.CanCapture, blockCans, id =>
            {
                if (id == "discatch") BattleClick(new BattleAction("catch"));
                else BattleClick(new BattleAction("item", id));
            }, () =>
            {
                battleMoreOpen = false;
                RebuildBattleControls();
            });
        }

        private void RenderDynamicBattleFrame()
        {
            if (arenaLayer == null || battle == null) return;
            GameUi.Clear(arenaLayer);
            GameUi.Clear(fxSpritesLayer);
            GameUi.Clear(hpHudLayer);
            GameUi.Clear(topHudLayer);
            fxBgVec.Clear();
            arenaAuraVec.Clear();
            fxFgVec.Clear();
            rainbowBorderVec.Clear();

            bool isSumsumBoss = battle.Encounter.Kind == "boss" && battle.Encounter.Captain == "sumsum";
            BattleEvent activeEv = battleWaiting && battleEventIndex < battleEvents.Length ? battleEvents[battleEventIndex] : null;

            foreach (var side in new[] { "enemy", "player" })
            {
                var mon = side == "enemy"
                    ? (viewActiveEnemy < viewEnemy.Count ? viewEnemy[viewActiveEnemy] : null)
                    : (viewActivePlayer < viewPlayer.Count ? viewPlayer[viewActivePlayer] : null);
                if (mon == null) continue;
                var box = PositionBox(side);
                var (ox, oy, alpha) = MonsterOffset(side, mon, activeEv);
                ui.Art(arenaLayer, "session/mon_shadow", box.Cx - box.W * 0.45f, box.Ground - 12f, box.W * 0.9f, (box.W * 0.9f * 60f) / 230f, true, 0.72f);
                if (side == "player" && !battleWaiting && battle.Outcome == null && mon.Hp > 0)
                    ui.Art(arenaLayer, "session/ui/turn", box.Cx - 82.5f, box.Ground - 28f, 165f, 55f, true);

                string darkPath = GameUi.Mon(mon.MonId, "front_dark");
                string path = side == "enemy" && isSumsumBoss && ui.ArtSize(darkPath) != Vector2.zero ? darkPath : GameUi.Mon(mon.MonId, side == "player" ? "back" : "front");
                Vector2 tex = ui.ArtSize(path);
                float height = tex.x > 0f ? Mathf.Min(box.H, (tex.y * box.W) / tex.x) : box.H;
                var spriteRect = new Rect(box.X + ox, box.Ground - height + oy, box.W, height);
                var eyeRect = spriteRect;
                if (tex.x > 0f && tex.y > 0f)
                {
                    float s = Mathf.Min(spriteRect.width / tex.x, spriteRect.height / tex.y);
                    float dw = tex.x * s, dh = tex.y * s;
                    eyeRect = new Rect(spriteRect.x + (spriteRect.width - dw) * 0.5f, spriteRect.y + (spriteRect.height - dh) * 0.5f, dw, dh);
                }

                if (mon.Mega && mon.Hp > 0) BattleFxView.RenderMegaAura(fxBgVec, box, battleTimeMs);
                BattleFxView.RenderRageBackFx(arenaLayer, ui, mon, box, battleTimeMs);

                bool confuseSplit = activeEv?.Type == "ability" && activeEv.Side == side &&
                                    (activeEv.Visuals ?? Array.Empty<VisualStep>()).Any(s => s.action.StartsWith("confuse", StringComparison.Ordinal));
                if (confuseSplit && mon.Hp > 0)
                {
                    float p = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                    float split = Mathf.Sin(Mathf.Clamp01(p / 0.85f) * Mathf.PI) * 30f;
                    ui.Art(arenaLayer, path, spriteRect.x - split, spriteRect.y, spriteRect.width, spriteRect.height, true, 0.65f);
                    ui.Art(arenaLayer, path, spriteRect.x + split, spriteRect.y, spriteRect.width, spriteRect.height, true, 0.65f);
                }
                else
                {
                    if (activeEv?.Type == "immune" && activeEv.Variant != "shield" && mon.Id == activeEv.Target && mon.Hp > 0)
                    {
                        float ip = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                        float dir = side == "enemy" ? 1f : -1f;
                        float trail = Mathf.Sin(ip * Mathf.PI) * 18f;
                        ui.Art(arenaLayer, path, spriteRect.x - dir * trail * 1.8f, spriteRect.y, spriteRect.width, spriteRect.height, true, 0.22f);
                        ui.Art(arenaLayer, path, spriteRect.x - dir * trail, spriteRect.y, spriteRect.width, spriteRect.height, true, 0.40f);
                    }
                    ui.Art(arenaLayer, path, spriteRect.x, spriteRect.y, spriteRect.width, spriteRect.height, true, mon.Hp <= 0 ? 0.28f : alpha);
                    if (mon.Mega && mon.Hp > 0) BattleFxView.RenderMegaEyes(arenaAuraVec, eyeRect, side == "player" ? "back" : "front", battleTimeMs, mon.MonId);
                }
                BattleFxView.RenderStatusFx(fxSpritesLayer, fxFgVec, ui, mon, box, battleTimeMs);
            }

            if (activeEv != null)
            {
                float progress = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                if (activeEv.Type == "ability")
                {
                    var steps = activeEv.Visuals ?? Array.Empty<VisualStep>();
                    float total = steps.Length > 0 ? Mathf.Max(1f, steps.Max(s => s.at + s.duration)) : 1f;
                    float t = progress * total;
                    string actorSide = string.IsNullOrEmpty(activeEv.Side) ? "player" : activeEv.Side;
                    var actorBox = PositionBox(actorSide);
                    foreach (var step in steps)
                    {
                        var (start, dur) = BattleFxView.StepSpan(step, total);
                        if (t < start || t > start + dur) continue;
                        string targetSide = step.action.EndsWith(":actor", StringComparison.Ordinal) ? actorSide : actorSide == "player" ? "enemy" : "player";
                        if (activeEv.Backfire && step.inHypno != "noChange") targetSide = targetSide == "player" ? "enemy" : "player";
                        BattleFxView.RenderAbilityFx(fxSpritesLayer, fxFgVec, ui, step.action, (t - start) / dur, actorBox, PositionBox(targetSide));
                    }
                }
                else if (activeEv.Type == "catch") BattleFxView.RenderCatchFx(fxSpritesLayer, ui, progress, PositionBox("enemy"), activeEv.Success);
                else if (activeEv.Type == "immune")
                {
                    string targetSide = viewEnemy.Any(m => m.Id == activeEv.Target) ? "enemy" : "player";
                    BattleFxView.RenderImmuneFx(fxFgVec, progress, PositionBox(targetSide), battleTimeMs, activeEv.Variant ?? "void");
                }
                else if (activeEv.Type == "mega")
                    BattleFxView.RenderMegaTransform(fxSpritesLayer, ui, progress, activeEv.MonId, battleTimeMs);
            }

            var foe = viewActiveEnemy < viewEnemy.Count ? viewEnemy[viewActiveEnemy] : null;
            var player = viewActivePlayer < viewPlayer.Count ? viewPlayer[viewActivePlayer] : null;
            var hudVec = VectorCanvas.Create("HudPillsVec", hpHudLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            if (foe != null)
            {
                HudWidgets.DrawMonPane(ui, fonts, data, hpHudLayer,
                    new MonHudView { MonId = foe.MonId, Level = foe.Level, Hp = foe.Hp, MaxHp = foe.MaxHp, Status = foe.Status, Rage = foe.Rage },
                    "big", 5f, 68f, hpTrail.TryGetValue(foe.Id, out float tr) ? tr : (float?)null, showHpText: true);
                if (GameData.HasPassive(foe.Passive)) DrawPassivePill(hudVec, hpHudLayer, foe, 158f, 68f, 112f, 22f);
                DrawStatStageChips(hudVec, hpHudLayer, foe, 158f, 93f, true);
            }
            if (player != null)
            {
                HudWidgets.DrawMonPane(ui, fonts, data, hpHudLayer,
                    new MonHudView { MonId = player.MonId, Level = player.Level, Hp = player.Hp, MaxHp = player.MaxHp, Status = player.Status, Rage = player.Rage },
                    "big", 165f, 443f, hpTrail.TryGetValue(player.Id, out float tr) ? tr : (float?)null, showHpText: true);
                if (player.Mega)
                {
                    hudVec.FillRoundRect(165f, 419f, 46f, 20f, 6f, GameUi.HexColor("#1f1047", 0.94f));
                    rainbowBorderVec.DrawRainbowBorder(165f, 419f, 46f, 20f, battleTimeMs, 2f, 6f, false);
                    fonts.Draw(hpHudLayer, "white_stroked_small", "MEGA", 188f, 424f, 8f, "center");
                }
                if (GameData.HasPassive(player.Passive)) DrawPassivePill(hudVec, hpHudLayer, player, 214f, 419f, 101f, 20f);
                DrawStatStageChips(hudVec, hpHudLayer, player, 89f, 444f, false);
            }

            if (battle.Encounter.Kind != "wild" && !string.IsNullOrEmpty(battle.Encounter.Captain))
            {
                int aliveEnemies = viewEnemy.Count(m => m.Hp > 0);
                string iconOverride = battle.Encounter.Kind == "starter_event" ? GameUi.Mon(battle.Encounter.Captain, "icon") : null;
                HudWidgets.DrawCaptainHud(ui, fonts, topHudLayer, battle.Encounter.Captain, viewEnemy.Count, aliveEnemies, 5f, 5f, iconOverride);
            }
            HudWidgets.DrawTopHud(ui, fonts, data, save, topHudLayer, false);
            var topHudVec = VectorCanvas.Create("TopHudVec", topHudLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            DrawBattleSpeedButton(topHudVec, topHudLayer);
            topHudVec.Commit();
            DrawFeedbacks();

            if (!string.IsNullOrEmpty(battleLog) && battleLogTimer > 0f && !battleMoreOpen)
                fonts.Draw(hpHudLayer, "white_stroked_big", battleLog, GameUi.Width * 0.5f, 124f, 17f, "center", null, GameUi.Width - 24f);

            foreach (var (rx, ry, rw, rh, phase, strong) in rainbowTargets)
                rainbowBorderVec.DrawRainbowBorder(rx, ry, rw, rh, battleTimeMs + phase, strong ? 3.5f : 2.8f, 9f, true);

            hudVec.Commit();
            fxBgVec.Commit();
            arenaAuraVec.Commit();
            fxFgVec.Commit();
            rainbowBorderVec.Commit();
        }

        private (float x, float y, float alpha) MonsterOffset(string side, CombatantView mon, BattleEvent activeEv)
        {
            float x = 0f, y = Mathf.Sin(battleTimeMs * 0.004f + (side == "enemy" ? 1.6f : 0f)) * 2f, alpha = 1f;
            if (activeEv?.Type == "ability")
            {
                float p = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                var steps = activeEv.Visuals ?? Array.Empty<VisualStep>();
                if (activeEv.Side == side && steps.Any(s => s.action.StartsWith("ram", StringComparison.Ordinal) || s.action.StartsWith("jump", StringComparison.Ordinal) || s.action.StartsWith("confuse", StringComparison.Ordinal)))
                {
                    float pulse = Mathf.Sin(p * Mathf.PI);
                    x += (side == "player" ? 14f : -14f) * pulse;
                    y += (steps.Any(s => s.action.StartsWith("jump", StringComparison.Ordinal)) ? -24f : -6f) * pulse;
                }
                if (activeEv.Side != side && steps.Any(s => s.action.StartsWith("evade", StringComparison.Ordinal)))
                    x += (side == "enemy" ? 28f : -28f) * Mathf.Sin(p * Mathf.PI);
            }
            if (activeEv?.Type == "miss" && mon.Id == activeEv.Target)
            {
                float p = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                x += Mathf.Sin(p * Mathf.PI) * (side == "enemy" ? 22f : -22f);
            }
            if (activeEv?.Type == "immune" && activeEv.Variant != "shield" && mon.Id == activeEv.Target)
            {
                float p = Mathf.Clamp01(battleEventTimer / Mathf.Max(1f, BattleEventDuration(activeEv)));
                x += Mathf.Sin(p * Mathf.PI) * (side == "enemy" ? 22f : -22f);
                alpha = 0.58f + 0.42f * Mathf.Abs(Mathf.Cos(p * Mathf.PI * 3f));
            }
            var dmg = feedbacks.LastOrDefault(f => f.Source.Type == "damage" && f.Source.Target == mon.Id);
            if (dmg != null)
            {
                x += Mathf.Sin(dmg.Elapsed / 20f) * 5f;
                alpha = (Mathf.FloorToInt(dmg.Elapsed / 60f) % 2) != 0 ? 0.4f : 1f;
            }
            return (x, y, alpha);
        }

        private void DrawPassivePill(VectorCanvas vec, Transform parent, CombatantView mon, float x, float y, float w, float h)
        {
            string key = GameData.PassiveKey(mon?.Passive);
            if (key == null) return;
            bool asleep = mon.Status == "sleep";
            int th = Math.Max(1, mon.Passive.threshold), ch = Math.Min(th, mon.Charges);
            bool ready = key == "charge" && ch >= th;
            bool depleted = (key == "shield" && mon.Shield <= 0) || ready;
            var (bg, accent, border) = HudWidgets.PassivePalette(key, asleep, depleted);

            vec.FillRoundRect(x, y, w, h, 7f, bg);
            vec.FillRect(x + 4f, y + 1.5f, w - 8f, 1.5f, new Color(accent.r, accent.g, accent.b, 0.3f));
            vec.StrokeRoundRect(x + 1f, y + 1f, w - 2f, h - 2f, 6.5f, border, ready ? 1.8f : 1.4f);

            float emblemCx = x + 10.5f, emblemCy = y + h * 0.5f;
            vec.FillCircle(emblemCx, emblemCy, 7.2f, new Color(0f, 0f, 0f, 0.38f));
            HudWidgets.DrawPassiveEmblem(vec, key, emblemCx, emblemCy, 4.6f, accent, bg);

            string prefix = !string.IsNullOrEmpty(mon.Passive.label) ? mon.Passive.label.ToUpperInvariant() : "VOID";
            float textX = x + 19f, textW = w - 23f, textMid = textX + textW * 0.5f;

            if (key == "charge")
            {
                string label = ready ? $"{prefix} READY!" : $"{prefix} {ch}/{th}";
                fonts.Draw(parent, "white_stroked_small", label, textMid, y + 3f, 6.8f, "center", ready ? accent : (Color?)null, textW);
                float gap = 2f, pipW = (textW - gap * (th - 1)) / th, pipY = y + h - 5.2f;
                for (int i = 0; i < th; i++)
                {
                    float px = textX + i * (pipW + gap);
                    Color pc = i < ch ? (ready ? GameUi.HexColor("#ffe052") : accent) : GameUi.HexColor("#06170e");
                    vec.FillRoundRect(px, pipY, pipW, 2.5f, 1f, pc);
                }
                return;
            }

            string textLabel;
            if (key == "shield")
            {
                textLabel = mon.Shield > 1 ? $"{prefix} x{mon.Shield}" : (mon.Shield > 0 ? $"{prefix} ON" : $"{prefix} OFF");
            }
            else if (key == "ramp")
            {
                int pct = GameData.Round(mon.PowerStacks * mon.Passive.dmgPerStack * 100);
                textLabel = mon.PowerStacks > 0 ? $"{prefix} x{mon.PowerStacks} {pct}%" : $"{prefix} {pct}%";
            }
            else
            {
                int chPct = GameData.Round(mon.Passive.chance * 100);
                int bonPct = GameData.Round(mon.VoidStacks * mon.Passive.dmgPerStack * 100);
                textLabel = mon.VoidStacks > 0 ? $"{prefix} x{mon.VoidStacks} {bonPct}%" : $"{prefix} {chPct}%";
            }
            fonts.Draw(parent, "white_stroked_small", textLabel, textMid, y + (h - 7.5f) * 0.5f, 7.5f, "center", null, textW);
        }

        private void DrawStatStageChips(VectorCanvas vec, Transform parent, CombatantView mon, float startX, float startY, bool horizontal)
        {
            if (mon == null || (mon.AtkMod == 0 && mon.DefMod == 0 && mon.AimMod == 0)) return;
            var entries = new[] { ("ATK", mon.AtkMod), ("DEF", mon.DefMod), ("AIM", mon.AimMod) };
            float chipW = horizontal ? 48f : 72f, chipH = 14.5f;
            int idx = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                var (stat, mod) = entries[i];
                if (mod == 0) continue;
                bool up = mod > 0;
                float cx = horizontal ? startX + idx * (chipW + 3f) : startX;
                float cy = horizontal ? startY : startY + idx * (chipH + 2f);
                Color bg = GameUi.HexColor(up ? "#0e2f1f" : "#381118", 0.92f);
                Color border = GameUi.HexColor(up ? "#5ee88b" : "#ff6b78", 0.94f);
                Color txt = GameUi.HexColor(up ? "#96ffb8" : "#ff9aa3");
                vec.FillRoundRect(cx, cy, chipW, chipH, 4.5f, bg);
                vec.StrokeRoundRect(cx + 0.5f, cy + 0.5f, chipW - 1f, chipH - 1f, 4f, border, 1f);
                string dir = up ? "UP" : "DN";
                fonts.Draw(parent, "white_stroked_small", $"{stat} {dir} {Math.Abs(mod)}", cx + chipW * 0.5f, cy + 3.5f, 6.4f, "center", txt, chipW - 4f);
                idx++;
            }
        }

        private void DrawBattleSpeedButton(VectorCanvas vec, Transform parent)
        {
            int speed = Mathf.Clamp(save.Speed, 1, 3);
            string fill = speed == 3 ? "#c92a2a" : speed == 2 ? "#c86a12" : "#123452";
            string border = speed == 3 ? "#ffd452" : speed == 2 ? "#ffe066" : "#75dbff";
            const float bx = 256f, by = 39f, bw = 58f, bh = 24f;
            vec.FillRoundRect(bx, by, bw, bh, 8f, GameUi.HexColor(fill, 0.9f));
            vec.StrokeRect(bx + 1f, by + 1f, bw - 2f, bh - 2f, GameUi.HexColor(border, 0.95f), 1.5f);
            Color bc = GameUi.HexColor(border);
            float midY = by + bh * 0.5f;
            for (int i = 0; i < speed; i++)
            {
                float tx = bx + 8f + i * 5f;
                vec.FillPoly(new[] { new Vector2(tx, midY - 4f), new Vector2(tx + 5f, midY), new Vector2(tx, midY + 4f) }, bc);
            }
            fonts.Draw(parent, "white_stroked_small", $"{speed}X", bx + bw - 8f, by + 7f, 10f, "right");
        }

        private void DrawFeedbacks()
        {
            int playerSlot = 0, enemySlot = 0;
            foreach (var fb in feedbacks)
            {
                var ev = fb.Source;
                bool isEnemy = viewEnemy.Any(m => m.Id == ev.Target);
                var box = PositionBox(isEnemy ? "enemy" : "player");
                float lift = (fb.Elapsed / fb.Duration) * 16f;
                int slot = isEnemy ? enemySlot++ : playerSlot++;
                float stackOffset = slot * 22f;
                string report = ev.Type == "critical" ? "critical" : ev.Type == "strong" ? "strong" : ev.Type == "weak" ? "weak" :
                                ev.Type == "hypno" ? "backfire" : ev.Type == "rage" ? "rage" :
                                (ev.Type == "status" && ev.Status == "sick") ? "sick" : null;
                if (report != null)
                    ui.Art(hpHudLayer, $"session/ui/report_{report}", box.Cx - 56f, box.Y + 22f - lift - stackOffset, 112f, 28f, true);
                else if (ev.Type == "miss")
                    fonts.Draw(hpHudLayer, "white_stroked_big", "MISS!", box.Cx, box.Y + 14f - lift - stackOffset, 17f, "center", GameUi.HexColor("#ffe066"));
                else if (ev.Type == "sleep")
                    fonts.Draw(hpHudLayer, "white_stroked_small", "ASLEEP!", box.Cx, box.Y + 14f - lift - stackOffset, 12f, "center", GameUi.HexColor("#d8b8ff"));
                else if (ev.Type == "immune")
                    fonts.Draw(hpHudLayer, "white_stroked_small", ev.Variant == "shield" ? "SHELL BROKEN" : "VOID IMMUNE",
                        box.Cx, box.Y + 8f - lift - stackOffset, 11f, "center", GameUi.HexColor(ev.Variant == "shield" ? "#8ef8ff" : "#e6b8ff"));
                else if (ev.Type == "damage")
                    fonts.Draw(hpHudLayer, "white_stroked_small", $"-{ev.Amount}", box.Cx, box.Y + 26f - lift - stackOffset, 19f, "center");
                else if (ev.Type == "heal")
                    fonts.Draw(hpHudLayer, "white_stroked_small", $"{ev.Amount} HP", box.Cx, box.Y + 26f - lift - stackOffset, 16f, "center", GameUi.HexColor("#7dff8b"));
                else if (ev.Type == "stat" && !string.IsNullOrEmpty(ev.Stat))
                {
                    bool up = ev.Amount > 0;
                    Color c = GameUi.HexColor(!up ? "#ff7d7d" : ev.Stat == "def" ? "#72e8ff" : ev.Stat == "aim" ? "#ffe066" : "#8aff8a");
                    fonts.Draw(hpHudLayer, "white_stroked_small", $"{ev.Stat.ToUpperInvariant()} {(up ? "UP" : "DOWN")} {Math.Abs(ev.Amount)}",
                        box.Cx, box.Y + 12f - lift - stackOffset, 12f, "center", c);
                }
            }
        }

        private List<(string itemId, int monIndex, OwnedMon mon)> ReadyTargetsForStone(string itemId)
        {
            var outList = new List<(string, int, OwnedMon)>();
            if (string.IsNullOrEmpty(itemId) || !itemId.StartsWith("levelup_", StringComparison.Ordinal)) return outList;
            string reqKind = itemId == "levelup_joker" ? null : itemId.Substring(8);
            for (int i = 0; i < save.Team.Count; i++)
            {
                var mon = save.Team[i];
                var info = data.Mon(mon.MonId);
                if (info == null) continue;
                int needXp = flow.NeededXp(mon);
                if (needXp > 0 && mon.Xp >= needXp && (reqKind == null || info.kind == reqKind))
                    outList.Add((itemId, i, mon));
            }
            return outList;
        }

        private bool IsItemUsable(string id, bool inBattle, bool allowCatch, bool blockCans)
        {
            if (string.IsNullOrEmpty(id) || save.Count(id) <= 0) return false;
            var item = data.Item(id);
            if (item == null) return false;
            if (inBattle)
            {
                if (!item.inBattle) return false;
                if (id == "discatch") return allowCatch;
                if (blockCans && id.StartsWith("can_", StringComparison.Ordinal)) return false;
                return true;
            }
            if (item.inBattle) return false;
            if (id == "healing_kit") return save.Team.Any(m => m.Hp < data.battleData.maxHP);
            if (id.StartsWith("levelup_", StringComparison.Ordinal)) return ReadyTargetsForStone(id).Count > 0;
            return false;
        }

        private void OpenPassiveInspect(string side)
        {
            if (battleWaiting || battle == null || battle.Outcome != null || itemsModalLayer == null) return;
            var mon = side == "enemy"
                ? (viewActiveEnemy < viewEnemy.Count ? viewEnemy[viewActiveEnemy] : null)
                : (viewActivePlayer < viewPlayer.Count ? viewPlayer[viewActivePlayer] : null);
            string key = GameData.PassiveKey(mon?.Passive);
            if (key == null) return;

            passiveInspectSide = side;
            ui.Sound("screen_open", save);
            GameUi.Clear(itemsModalLayer);
            GameUi.Fill(itemsModalLayer, new Color(0.02f, 0.015f, 0.06f, 0.70f), 0f, 0f, GameUi.Width, GameUi.Height, true);
            ui.HitBox(itemsModalLayer, 0f, 0f, GameUi.Width, GameUi.Height, () =>
            {
                ui.Sound("screen_close", save);
                passiveInspectSide = null;
                GameUi.Clear(itemsModalLayer);
            });

            var p = mon.Passive;
            bool asleep = mon.Status == "sleep";
            int th = Math.Max(1, p.threshold), ch = Math.Min(th, mon.Charges);
            bool ready = key == "charge" && ch >= th;
            bool depleted = (key == "shield" && mon.Shield <= 0) || ready;
            var (bg, accent, border) = HudWidgets.PassivePalette(key, asleep, depleted);

            const float cw = 280f, chCard = 166f;
            float cx = (GameUi.Width - cw) * 0.5f, cy = (GameUi.Height - chCard) * 0.5f;
            var vec = VectorCanvas.Create("PassiveInspectVec", itemsModalLayer, cx, cy, cw, chCard);
            vec.FillRoundRect(0f, 0f, cw, chCard, 12f, bg);
            vec.FillRect(6f, 2f, cw - 12f, 4f, new Color(accent.r, accent.g, accent.b, 0.35f));
            vec.StrokeRoundRect(1.5f, 1.5f, cw - 3f, chCard - 3f, 11f, border, 2f);
            vec.FillCircle(30f, 34f, 17f, new Color(0f, 0f, 0f, 0.45f));
            vec.StrokeCircle(30f, 34f, 17f, accent, 1.5f, 0f, Mathf.PI * 2f, 16);
            HudWidgets.DrawPassiveEmblem(vec, key, 30f, 34f, 9.5f, accent, bg);

            string ownerTag = side == "enemy" ? "ENEMY TRAIT" : "YOUR TRAIT";
            string statusTag;
            if (key == "shield") statusTag = mon.Shield > 0 ? $"SHIELD X{mon.Shield}" : "SHATTERED";
            else if (key == "ramp") statusTag = $"STACKS X{mon.PowerStacks}";
            else if (key == "charge") statusTag = ready ? "FULL POWER" : $"CHARGE {ch}/{th}";
            else statusTag = $"EVADES X{mon.VoidStacks}";

            vec.FillRoundRect(16f, 64f, cw - 32f, 24f, 6f, new Color(0f, 0f, 0f, 0.35f));
            if (key == "charge")
            {
                float barGap = 4f, barW = (cw - 48f - barGap * (th - 1)) / th;
                for (int i = 0; i < th; i++)
                {
                    Color pc = i < ch ? (ready ? GameUi.HexColor("#ffe052") : accent) : GameUi.HexColor("#06170e");
                    vec.FillRoundRect(24f + i * (barW + barGap), 71f, barW, 10f, 3f, pc);
                }
            }
            vec.Commit();

            fonts.Draw(itemsModalLayer, "white_stroked_small", GameData.PassiveTitle(p), cx + 56f, cy + 16f, 11f, "left", accent, 170f);
            fonts.Draw(itemsModalLayer, "white_stroked_small", ownerTag, cx + 56f, cy + 33f, 8f, "left", GameUi.HexColor("#cbd5e1"));
            fonts.Draw(itemsModalLayer, "white_stroked_small", statusTag, cx + cw - 18f, cy + 33f, 8f, "right", accent);

            if (key != "charge")
            {
                string metricText = key == "shield"
                    ? (asleep ? "INACTIVE WHILE ASLEEP" : (mon.Shield > 0 ? "NEXT HOSTILE HIT BLOCKED" : "SHIELD ALREADY SPENT"))
                    : key == "ramp"
                        ? $"CURRENT BONUS: {GameData.Round(mon.PowerStacks * p.dmgPerStack * 100)}% DAMAGE UP"
                        : $"{GameData.Round(p.chance * 100)}% EVADE - BONUS: {GameData.Round(mon.VoidStacks * p.dmgPerStack * 100)}% UP";
                fonts.Draw(itemsModalLayer, "white_stroked_small", metricText, cx + cw * 0.5f, cy + 71f, 8.5f, "center", null, cw - 40f);
            }

            fonts.DrawLines(itemsModalLayer, "white_small", GameData.PassiveDesc(p), cx + cw * 0.5f, cy + 98f, 9f, cw - 36f, 13f, "center");
            fonts.Draw(itemsModalLayer, "white_stroked_small", "TAP ANYWHERE TO CLOSE", cx + cw * 0.5f, cy + chCard - 18f, 7.5f, "center", GameUi.HexColor("#a9b8cc"));
            ui.ArtButton(itemsModalLayer, "general/btn_close", cx + cw - 24f, cy - 12f, 36f, 36f, () =>
            {
                ui.Sound("screen_close", save);
                passiveInspectSide = null;
                GameUi.Clear(itemsModalLayer);
            });
        }

        private void OpenItemsOverlay(bool inBattle, bool allowCatch, bool blockCans, Action<string> onUse, Action onClose, RectTransform customHost = null)
        {
            var host = customHost ?? itemsModalLayer;
            if (host == null) return;
            passiveInspectSide = null;
            modalItemPage = 0;
            modalItemFilter = 0;
            modalSelectedItemId = null;
            modalPickingStoneTarget = false;
            modalStoneId = null;
            RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
        }

        private void RenderItemsModalContents(Transform host, bool inBattle, bool allowCatch, bool blockCans, Action<string> onUse, Action onClose)
        {
            GameUi.Clear(host);
            GameUi.Fill(host, new Color(0.02f, 0.015f, 0.06f, 0.68f), 0f, 0f, GameUi.Width, GameUi.Height, true);

            if (modalPickingStoneTarget && !string.IsNullOrEmpty(modalStoneId))
            {
                GameUi.Fill(host, new Color(0f, 0f, 0f, 0.45f), 0f, 0f, GameUi.Width, GameUi.Height, true);
                fonts.Draw(host, "white_stroked_big", "CHOOSE DYNAMON", GameUi.Width * 0.5f, 96f, 18f, "center");
                fonts.Draw(host, "white_stroked_small", "TAP A READY DYNAMON TO LEVEL UP", GameUi.Width * 0.5f, 122f, 8.5f, "center", GameUi.HexColor("#ffe066"));
                var targets = ReadyTargetsForStone(modalStoneId).Take(6).ToArray();
                for (int i = 0; i < targets.Length; i++)
                {
                    int monIdx = targets[i].monIndex;
                    float sx = 30f + (i % 2) * 140f, sy = 152f + (i / 2) * 74f;
                    HudWidgets.DrawMonPane(ui, fonts, data, host, MonHudView.FromOwned(data, targets[i].mon), "thumb", sx, sy, showLevelUp: true);
                    ui.HitBox(host, sx, sy, 120f, 65f, () =>
                    {
                        ui.Sound("cool_button", save);
                        string chosenStone = modalStoneId;
                        modalPickingStoneTarget = false;
                        onUse?.Invoke(chosenStone + ":" + monIdx);
                    });
                }
                ui.ArtButton(host, "general/btn_cancel", (GameUi.Width - 100f) * 0.5f, 404f, 100f, 45f, () =>
                {
                    ui.Sound("screen_close", save);
                    modalPickingStoneTarget = false;
                    RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                });
                return;
            }

            var order = data.items.Select((it, idx) => (it.id, idx)).ToDictionary(x => x.id, x => x.idx);
            var allOwned = save.Inventory
                .Where(row => row.amount > 0 && (!inBattle || (data.Item(row.id)?.inBattle == true && (row.id != "discatch" || allowCatch))))
                .OrderBy(row => order.TryGetValue(row.id, out int idx) ? idx : 999)
                .ToArray();

            var entries = inBattle || modalItemFilter == 0
                ? allOwned
                : allOwned.Where(row =>
                {
                    bool battleItem = data.Item(row.id)?.inBattle == true;
                    return modalItemFilter == 1 ? !battleItem : battleItem;
                }).ToArray();

            int totalPages = Math.Max(1, (entries.Length + 7) / 8);
            modalItemPage = GameData.Clamp(modalItemPage, 0, totalPages - 1);
            var pageItems = entries.Skip(modalItemPage * 8).Take(8).ToArray();
            if (string.IsNullOrEmpty(modalSelectedItemId) || !entries.Any(e => e.id == modalSelectedItemId))
                modalSelectedItemId = pageItems.FirstOrDefault()?.id;

            const float bgX = 15f, bgW = 290f, bgH = 285f;
            float bgY = inBattle ? 128f : 122f;

            var chromeVec = VectorCanvas.Create("BagChromeVec", host, 0f, 0f, GameUi.Width, GameUi.Height);
            var slotOverlay = GameUi.Box("BagSlotOverlays", host, 0f, 0f, GameUi.Width, GameUi.Height);
            var slotBadgeVec = VectorCanvas.Create("BagSlotBadgeVec", slotOverlay, 0f, 0f, GameUi.Width, GameUi.Height);

            if (!inBattle)
            {
                var tabs = new[] { ("ALL", 0), ("MAP USE", 1), ("BATTLE", 2) };
                for (int t = 0; t < tabs.Length; t++)
                {
                    var (label, filterVal) = tabs[t];
                    bool activeTab = modalItemFilter == filterVal;
                    float tx = 24f + t * 92f, ty = 90f, tw = 86f, th = 24f;
                    chromeVec.FillRoundRect(tx, ty, tw, th, 8f, GameUi.HexColor(activeTab ? "#247d3d" : "#162033", 0.95f));
                    chromeVec.StrokeRoundRect(tx + 1f, ty + 1f, tw - 2f, th - 2f, 7f, GameUi.HexColor(activeTab ? "#9cf284" : "#3a506e", 0.95f), activeTab ? 1.8f : 1.2f);
                    fonts.Draw(slotOverlay, "white_stroked_small", label, tx + tw * 0.5f, ty + 7f, 8.5f, "center",
                        activeTab ? GameUi.HexColor("#ffffff") : GameUi.HexColor("#9db0c7"));
                    int fv = filterVal;
                    ui.HitBox(host, tx, ty, tw, th, () =>
                    {
                        if (modalItemFilter == fv) return;
                        ui.Sound("button_click", save);
                        modalItemFilter = fv;
                        modalItemPage = 0;
                        modalSelectedItemId = null;
                        RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                    });
                }
            }

            ui.Art(host, "items/bg_items", bgX, bgY, bgW, bgH);
            chromeVec.transform.SetAsLastSibling();

            // Page indicator badge on the right side of the green header (clear of backpack and close button)
            float pgX = bgX + bgW - 86f, pgY = bgY + 23f;
            chromeVec.FillRoundRect(pgX, pgY, 48f, 18f, 6f, GameUi.HexColor("#123b1a", 0.92f));
            chromeVec.StrokeRoundRect(pgX + 1f, pgY + 1f, 46f, 16f, 5f, GameUi.HexColor("#7df26b", 0.9f), 1.2f);
            fonts.Draw(slotOverlay, "white_stroked_small", $"{modalItemPage + 1}/{totalPages}", pgX + 24f, pgY + 4.5f, 8f, "center");

            ui.ArtButton(host, "general/btn_close", bgX + bgW - 28f, bgY - 2f, 38f, 38f, () =>
            {
                ui.Sound("screen_close", save);
                GameUi.Clear(host);
                onClose?.Invoke();
            });

            for (int i = 0; i < 8; i++)
            {
                float cx = GameUi.Width * 0.5f - 66f * 1.5f + (i % 4) * 66f;
                float cy = bgY + 93f + (i / 4) * 70f;
                if (i < pageItems.Length)
                {
                    var row = pageItems[i];
                    var slotItem = data.Item(row.id);
                    bool slotUsable = IsItemUsable(row.id, inBattle, allowCatch, blockCans);
                    bool isSel = row.id == modalSelectedItemId;

                    ui.ArtButton(host, "items/item_slot", cx - 27.5f, cy - 27.5f, 55f, 55f, () =>
                    {
                        ui.Sound("button_click", save);
                        modalSelectedItemId = row.id;
                        RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                    });
                    if (isSel) ui.Art(host, "items/item_selected", cx - 30f, cy - 30f, 60f, 60f);
                    ui.Art(host, GameUi.Item(row.id), cx - 19f, cy - 22f, 38f, 38f, true, slotUsable ? 1f : 0.48f);

                    if (slotUsable)
                    {
                        slotBadgeVec.FillCircle(cx - 20f, cy - 19f, 4f, GameUi.HexColor("#31e069"));
                        slotBadgeVec.StrokeCircle(cx - 20f, cy - 19f, 4f, GameUi.HexColor("#0d381b"), 1.2f, 0f, Mathf.PI * 2f, 10);
                    }
                    else if (!inBattle && slotItem?.inBattle == true)
                    {
                        slotBadgeVec.FillRoundRect(cx - 26f, cy - 25f, 22f, 11f, 4f, GameUi.HexColor("#173a5e", 0.94f));
                        slotBadgeVec.StrokeRoundRect(cx - 25.5f, cy - 24.5f, 21f, 10f, 3.5f, GameUi.HexColor("#62bfff", 0.9f), 1f);
                        fonts.Draw(slotOverlay, "white_stroked_small", "BTL", cx - 15f, cy - 23f, 6f, "center", GameUi.HexColor("#9ee0ff"));
                    }
                    else if (row.id.StartsWith("levelup_", StringComparison.Ordinal))
                    {
                        slotBadgeVec.FillRoundRect(cx - 26f, cy - 25f, 20f, 11f, 4f, GameUi.HexColor("#54310f", 0.94f));
                        slotBadgeVec.StrokeRoundRect(cx - 25.5f, cy - 24.5f, 19f, 10f, 3.5f, GameUi.HexColor("#e39d3b", 0.9f), 1f);
                        fonts.Draw(slotOverlay, "white_stroked_small", "XP", cx - 16f, cy - 23f, 6f, "center", GameUi.HexColor("#ffe08a"));
                    }

                    string qtyText = $"x{row.amount}";
                    float pillW = row.amount >= 100 ? 31f : row.amount >= 10 ? 27f : 23f;
                    float px = cx + 27f - pillW, py = cy + 12f;
                    slotBadgeVec.FillRoundRect(px, py, pillW, 14f, 5f, GameUi.HexColor("#0c2236", 0.95f));
                    slotBadgeVec.StrokeRoundRect(px + 0.5f, py + 0.5f, pillW - 1f, 13f, 4.5f,
                        GameUi.HexColor(isSel ? "#ffd447" : "#49b7e0", 0.95f), isSel ? 1.4f : 1.1f);
                    fonts.Draw(slotOverlay, "white_stroked_small", qtyText, px + pillW * 0.5f, py + 3f, 7.5f, "center");
                }
                else
                {
                    ui.Art(host, "items/item_slot", cx - 27.5f, cy - 27.5f, 55f, 55f, false, 0.55f);
                }
            }
            slotBadgeVec.Commit();

            float useY = bgY + bgH - 15f;
            if (totalPages > 1)
            {
                bool hasPrev = modalItemPage > 0, hasNext = modalItemPage < totalPages - 1;
                float prevX = bgX + 4f, nextX = bgX + bgW - 78f, navY = useY + 7.5f;

                chromeVec.FillRoundRect(prevX, navY, 74f, 30f, 8f, GameUi.HexColor(hasPrev ? "#163b5c" : "#121a24", 0.95f));
                chromeVec.StrokeRoundRect(prevX + 1f, navY + 1f, 72f, 28f, 7f, GameUi.HexColor(hasPrev ? "#5cd6ff" : "#38485c", 0.95f), 1.5f);
                Color pCol = GameUi.HexColor(hasPrev ? "#7ce4ff" : "#56687a");
                chromeVec.FillPoly(new[] { new Vector2(prevX + 12f, navY + 15f), new Vector2(prevX + 20f, navY + 10f), new Vector2(prevX + 20f, navY + 20f) }, pCol);
                fonts.Draw(slotOverlay, "white_stroked_small", "PREV", prevX + 46f, navY + 10f, 8.5f, "center", hasPrev ? (Color?)null : pCol);
                if (hasPrev)
                {
                    ui.HitBox(host, prevX, navY, 74f, 30f, () =>
                    {
                        ui.Sound("button_click", save);
                        modalItemPage--;
                        modalSelectedItemId = null;
                        RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                    });
                }

                chromeVec.FillRoundRect(nextX, navY, 74f, 30f, 8f, GameUi.HexColor(hasNext ? "#163b5c" : "#121a24", 0.95f));
                chromeVec.StrokeRoundRect(nextX + 1f, navY + 1f, 72f, 28f, 7f, GameUi.HexColor(hasNext ? "#5cd6ff" : "#38485c", 0.95f), 1.5f);
                Color nCol = GameUi.HexColor(hasNext ? "#7ce4ff" : "#56687a");
                chromeVec.FillPoly(new[] { new Vector2(nextX + 62f, navY + 15f), new Vector2(nextX + 54f, navY + 10f), new Vector2(nextX + 54f, navY + 20f) }, nCol);
                fonts.Draw(slotOverlay, "white_stroked_small", "NEXT", nextX + 28f, navY + 10f, 8.5f, "center", hasNext ? (Color?)null : nCol);
                if (hasNext)
                {
                    ui.HitBox(host, nextX, navY, 74f, 30f, () =>
                    {
                        ui.Sound("button_click", save);
                        modalItemPage++;
                        modalSelectedItemId = null;
                        RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                    });
                }
            }

            bool canUse = IsItemUsable(modalSelectedItemId, inBattle, allowCatch, blockCans);
            ui.ArtButton(host, "items/btn_use", (GameUi.Width - 105f) * 0.5f, useY, 105f, 45f, () =>
            {
                if (!IsItemUsable(modalSelectedItemId, inBattle, allowCatch, blockCans)) return;
                if (!inBattle && modalSelectedItemId.StartsWith("levelup_", StringComparison.Ordinal))
                {
                    var targets = ReadyTargetsForStone(modalSelectedItemId);
                    if (targets.Count > 1)
                    {
                        ui.Sound("button_click", save);
                        modalPickingStoneTarget = true;
                        modalStoneId = modalSelectedItemId;
                        RenderItemsModalContents(host, inBattle, allowCatch, blockCans, onUse, onClose);
                        return;
                    }
                    if (targets.Count == 1) { GameUi.Clear(host); onUse?.Invoke(modalSelectedItemId + ":" + targets[0].monIndex); return; }
                }
                GameUi.Clear(host);
                onUse?.Invoke(modalSelectedItemId);
            }, canUse);

            if (!string.IsNullOrEmpty(modalSelectedItemId))
            {
                ui.Art(slotOverlay, GameUi.Item(modalSelectedItemId), GameUi.Width * 0.5f - 36f, useY + 9.5f, 26f, 26f, true, canUse ? 1f : 0.45f);
                var item = data.Item(modalSelectedItemId);
                string tagText = "READY", tagBg = "#1c6b36", tagBd = "#5df08c";
                string helperText = string.Empty;

                if (canUse)
                {
                    if (!inBattle && modalSelectedItemId == "healing_kit")
                    {
                        int injured = save.Team.Count(m => m.Hp < data.battleData.maxHP);
                        helperText = $"{injured} OF {save.Team.Count} DYNAMONS INJURED";
                    }
                    else if (!inBattle && modalSelectedItemId.StartsWith("levelup_", StringComparison.Ordinal))
                    {
                        int readyCount = ReadyTargetsForStone(modalSelectedItemId).Count;
                        helperText = readyCount == 1 ? "1 DYNAMON READY TO LEVEL UP" : $"{readyCount} DYNAMONS READY TO LEVEL UP";
                    }
                    else if (inBattle)
                    {
                        helperText = "TAP USE TO ACTIVATE THIS TURN";
                    }
                }
                else if (!inBattle && item?.inBattle == true)
                {
                    tagText = "BATTLE"; tagBg = "#194a73"; tagBd = "#5ec4ff";
                    helperText = "OPEN BAG DURING BATTLE TO USE";
                }
                else if (!inBattle && modalSelectedItemId.StartsWith("levelup_", StringComparison.Ordinal))
                {
                    tagText = "NEED XP"; tagBg = "#6b4011"; tagBd = "#ffc15e";
                    helperText = "FILL DYNAMON XP TO 100% FIRST";
                }
                else if (!inBattle && modalSelectedItemId == "healing_kit")
                {
                    tagText = "FULL HP"; tagBg = "#2b3b4a"; tagBd = "#8ca9c4";
                    helperText = "ALL DYNAMONS ARE AT FULL HP";
                }
                else if (modalSelectedItemId == "starter_ticket")
                {
                    tagText = "PASS"; tagBg = "#472c5e"; tagBd = "#c88cff";
                    helperText = "USE STARTER EVENT ON MAP TO ENTER";
                }
                else if (inBattle && blockCans && modalSelectedItemId.StartsWith("can_", StringComparison.Ordinal))
                {
                    tagText = "ASLEEP"; tagBg = "#571f73"; tagBd = "#d88aff";
                    helperText = "CANNOT USE CANS WHILE ASLEEP";
                }

                float tagX = bgX + bgW - 82f, tagY = bgY + 217f;
                chromeVec.FillRoundRect(tagX, tagY, 62f, 15f, 5f, GameUi.HexColor(tagBg, 0.96f));
                chromeVec.StrokeRoundRect(tagX + 0.5f, tagY + 0.5f, 61f, 14f, 4.5f, GameUi.HexColor(tagBd, 0.95f), 1.2f);
                fonts.Draw(slotOverlay, "white_stroked_small", tagText, tagX + 31f, tagY + 3.5f, 7f, "center", GameUi.HexColor(tagBd));

                fonts.Draw(slotOverlay, "white_stroked_small", (item?.title ?? modalSelectedItemId).ToUpperInvariant(),
                    bgX + 19f, bgY + 219f, 9f, "left", null, 182f);
                fonts.DrawLines(slotOverlay, "white_small", item?.desc ?? "Item details are unavailable.",
                    bgX + 19f, bgY + 235f, 8f, 252f, 11f, "left");

                if (!string.IsNullOrEmpty(helperText))
                {
                    float hy = useY + 52f;
                    chromeVec.FillRoundRect(24f, hy, GameUi.Width - 48f, 22f, 7f, GameUi.HexColor("#101b2e", 0.92f));
                    chromeVec.StrokeRoundRect(25f, hy + 1f, GameUi.Width - 50f, 20f, 6f, GameUi.HexColor(tagBd, 0.65f), 1.2f);
                    fonts.Draw(slotOverlay, "white_stroked_small", helperText, GameUi.Width * 0.5f, hy + 6.5f, 7.8f, "center", GameUi.HexColor(tagBd), GameUi.Width - 64f);
                }
            }
            else
            {
                bool filteredEmpty = !inBattle && allOwned.Length > 0;
                fonts.Draw(slotOverlay, "black_small", filteredEmpty ? "NO ITEMS IN THIS TAB" : "YOUR BAG IS EMPTY",
                    GameUi.Width * 0.5f, bgY + 115f, 10.5f, "center");
                fonts.Draw(slotOverlay, "white_small", filteredEmpty ? "Switch filter tab to view other items." : "Find items in battles and loot bags.",
                    GameUi.Width * 0.5f, bgY + 228f, 9f, "center");
            }
            slotOverlay.transform.SetAsLastSibling();
            chromeVec.Commit();
        }
    }
}
