using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamons
{
    public sealed class Combatant
    {
        public string Id, MonId, Kind, Status;
        public int Level, Hp, MaxHp, Xp, StatCap = 150, DamageCap, SleepTurns, RageTurns, Shield, VoidStacks, PowerStacks, Charges;
        public bool Sparring, Mega, Rage, SleepFresh;
        public Stats Base, Mods;
        public Passive Passive;
        public MoveData[] Moves;
        public readonly Dictionary<string, int> Cooldowns = new Dictionary<string, int>();
        public bool Ready(MoveData move) => !Cooldowns.TryGetValue(move.id, out int turns) || turns == 0;
        public int Stat(string key, bool raw = false)
        {
            int basis = key == "atk" ? Base.atk : key == "def" ? Base.def : Base.aim;
            if (raw) return basis;
            int mod = key == "atk" ? Mods.atk : key == "def" ? Mods.def : Mods.aim;
            int value = GameData.Clamp(basis + mod, 1, StatCap);
            return Mega ? GameData.Round(value * 1.3) : value;
        }
    }

    public readonly struct BattleAction
    {
        public readonly string Type, Id;
        public readonly int Index;
        public BattleAction(string type, string id = null, int index = -1) { Type = type; Id = id; Index = index; }
    }

    public sealed class BattleEvent
    {
        public string Type, Target, Text, Stat, Actor;
        public string AbilityId, Side, MonId, Variant, Status;
        public bool FullPower, Backfire, Missed, Success, Ready;
        public int Amount, Hp = -1, ShieldHits, Stacks, BonusPct, Charges, Threshold;
        public VisualStep[] Visuals;
    }

    public sealed partial class BattleRules
    {
        private readonly GameData data;
        private readonly Func<double> rng;
        private readonly HashSet<Combatant> participants = new HashSet<Combatant>(), training = new HashSet<Combatant>(), awarded = new HashSet<Combatant>();
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private int enemyHeals, enemyAntidotes, enemyPeppers, enemyBerries, lastSwitchTurn = -99;
        public Encounter Encounter { get; }
        public Combatant[] Party { get; }
        public Combatant[] Opponents { get; }
        public int PlayerSlot { get; private set; }
        public int EnemySlot { get; private set; }
        public Combatant Player => Party[PlayerSlot];
        public Combatant Enemy => Opponents[EnemySlot];
        public int Turn { get; private set; } = 1;
        public string Outcome { get; private set; }
        public Combatant Caught { get; private set; }
        public bool NeedsSwitch => Outcome == null && Player.Hp <= 0 && Party.Any(mon => mon.Hp > 0);
        public bool CanMega => Outcome == null && Player.Hp > 0 && Player.Status != "sleep" && !Party.Any(mon => mon.Mega);

        public BattleRules(GameData data, Encounter encounter, IReadOnlyList<OwnedMon> party, Func<double> rng)
        {
            this.data = data; this.rng = rng; Encounter = encounter;
            Party = party.Select(mon => Create(new MemberSpec { MonId = mon.MonId, Level = mon.Level }, mon.Id, mon.Hp, mon.Xp)).ToArray();
            Opponents = encounter.Members.Select((spec, i) => Create(spec, "enemy-" + i)).ToArray();
            PlayerSlot = Array.FindIndex(Party, mon => mon.Hp > 0);
            if (PlayerSlot < 0 || Opponents.Length == 0) throw new ArgumentException("A battle needs both sides alive.");
            training.Add(Player);
            enemyHeals = encounter.Ai?.Heal ?? 0; enemyAntidotes = encounter.Ai?.Antidote ?? 0;
            enemyPeppers = encounter.Ai?.Pepper ?? 0; enemyBerries = encounter.Ai?.Berry ?? 0;
        }

        private Combatant Create(MemberSpec spec, string id, int? hp = null, int xp = 0)
        {
            var mon = data.Mon(spec.MonId);
            var basis = spec.Stats ?? data.StatsAt(mon.id, spec.Level, !spec.Sparring);
            if (spec.Sparring) basis.def = Math.Min(46, basis.def);
            int max = spec.MaxHp > 0 ? spec.MaxHp : data.battleData.maxHP;
            var passive = GameData.HasPassive(mon.passive) ? mon.passive : null;
            return new Combatant { Id = id, MonId = mon.id, Kind = mon.kind, Level = spec.Level, Base = basis,
                MaxHp = max, Hp = GameData.Clamp(hp ?? max, 0, max), Xp = xp, Sparring = spec.Sparring, DamageCap = spec.DamageCap,
                StatCap = spec.StatCap, Passive = passive, Shield = passive?.type == "shield" ? Math.Max(1, passive.shieldHits) : 0,
                Moves = data.MovesAt(mon.id, spec.Level) };
        }

        public int Damage(Combatant actor, Combatant target, MoveData move, bool crit = false, bool backfire = false, bool item = false)
        {
            double ratio = actor.Stat("atk") / (double)Math.Max(1, target.Stat("def"));
            if (item) ratio = Math.Max(1, ratio);
            int level = target.Sparring ? Math.Min(16, target.Level) : target.Level;
            int stacks = actor.Passive?.type == "ramp" ? actor.PowerStacks : actor.VoidStacks;
            double raw = move.power * data.KindFactor(move.kind, target.Kind) * ratio * Math.Max(.2, (40 - level) / 40.0)
                * (crit && !item ? data.battleData.critFactor : 1) * (backfire && !item ? .5 : 1)
                * (actor.Rage ? data.battleData.rageFactor : 1) * (1 + stacks * (actor.Passive?.dmgPerStack ?? 0));
            if (!actor.Sparring) return Math.Max(1, GameData.Round(raw));
            int cap = actor.DamageCap > 0 ? actor.DamageCap : 26;
            return GameData.Clamp(GameData.Round(raw * .55), 6, GameData.Round(cap * (crit && !item ? 1.35 : actor.Mega ? 1.2 : 1)));
        }

        private string SideOf(Combatant mon) => mon == null ? null : Party.Contains(mon) ? "player" : "enemy";

        private void Emit(string type, Combatant target = null, string text = null, int amount = 0, bool hp = false, string stat = null,
            string abilityId = null, bool fullPower = false, bool backfire = false, bool missed = false, bool success = false, bool ready = false,
            string variant = null, string status = null, int shieldHits = 0, int stacks = 0, int bonusPct = 0, int charges = 0, int threshold = 0) =>
            events.Add(new BattleEvent
            {
                Type = type, Target = target?.Id, Side = SideOf(target), MonId = target?.MonId,
                Text = text, Amount = amount, Hp = hp ? target.Hp : -1, Stat = stat,
                AbilityId = abilityId, FullPower = fullPower, Backfire = backfire, Missed = missed,
                Success = success, Ready = ready, Variant = variant, Status = status,
                ShieldHits = shieldHits, Stacks = stacks, BonusPct = bonusPct, Charges = charges, Threshold = threshold
            });

        public BattleEvent[] Apply(BattleAction action, SaveData inventory = null)
        {
            events.Clear();
            if (Outcome != null) return Array.Empty<BattleEvent>();
            if (action.Type == "escape") { Finish("escaped"); return events.ToArray(); }
            if (action.Type == "switch")
            {
                if (action.Index < 0 || action.Index >= Party.Length || action.Index == PlayerSlot || Party[action.Index].Hp <= 0) return Array.Empty<BattleEvent>();
                PlayerSlot = action.Index; training.Add(Player); Emit("switch", Player, "GO " + Player.MonId.ToUpperInvariant());
                return events.ToArray();
            }
            if (NeedsSwitch) return Array.Empty<BattleEvent>();
            var actor = Player; var target = Enemy;
            if (action.Type == "mega")
            {
                if (!CanMega) return Array.Empty<BattleEvent>();
                actor.Mega = true; Emit("mega", actor, "MEGA EVOLUTION!"); return events.ToArray();
            }
            MoveData move = null;
            if (action.Type == "ability")
            {
                move = actor.Moves.FirstOrDefault(it => it.id == action.Id);
                if (move == null || !actor.Ready(move)) return Array.Empty<BattleEvent>();
            }
            else if (action.Type == "item")
            {
                if (string.IsNullOrEmpty(action.Id) || data.Item(action.Id)?.inBattle != true ||
                    (actor.Status == "sleep" && action.Id.StartsWith("can_", StringComparison.Ordinal))) return Array.Empty<BattleEvent>();
                move = data.Move(action.Id.StartsWith("can_", StringComparison.Ordinal) ? "can-" + action.Id.Substring(4) : "item-" + action.Id);
                if (move == null || inventory?.Take(action.Id) != true) return Array.Empty<BattleEvent>();
            }
            else if (action.Type != "catch" || !Encounter.CanCapture || inventory?.Take("discatch") != true) return Array.Empty<BattleEvent>();

            participants.Add(actor); training.Add(actor);
            if (action.Type == "catch")
            {
                bool success = rng() < CatchOdds(target);
                Emit("catch", target, success ? "CAUGHT!" : "BROKE FREE!", success: success);
                if (success) { Caught = target; Award(target); Finish("player"); return events.ToArray(); }
            }
            else if (action.Type == "ability" && actor.Status == "sleep") Emit("sleep", actor, "FAST ASLEEP", status: "sleep");
            else Resolve(actor, target, move);

            if (CheckEnd()) return events.ToArray();
            if (actor.Hp <= 0) { participants.Remove(actor); Tick(Party, actor, false); Turn++; return events.ToArray(); }
            if (SwitchEnemy()) { Tick(Party, actor, true); CheckEnd(); Turn++; return events.ToArray(); }
            EnemyTurn();
            if (CheckEnd()) return events.ToArray();
            if (SwitchEnemy()) { Tick(Party, actor, true); CheckEnd(); Turn++; return events.ToArray(); }
            if (actor.Hp <= 0) participants.Remove(actor);
            Tick(Party, actor, actor.Hp > 0);
            Tick(Opponents, Enemy, true);
            if (!CheckEnd()) { SwitchEnemy(); Turn++; }
            return events.ToArray();
        }

        private double CatchOdds(Combatant target)
        {
            var bands = data.Mon(target.MonId).catchOddsRanges;
            var points = (bands.LastOrDefault(band => band.fromLvl <= target.Level) ?? bands.FirstOrDefault())?.data;
            if (points == null || points.Length == 0) return 0;
            double hp = target.Hp / (double)target.MaxHp;
            var high = points[0]; var low = points[points.Length - 1];
            if (hp <= low.at) return low.odds;
            if (hp >= high.at) return points.Length == 1 ? 0 : high.odds;
            return low.odds + (hp - low.at) * (high.odds - low.odds) / (high.at - low.at);
        }

        private void Tick(Combatant[] team, Combatant active, bool sick)
        {
            if (active.Hp > 0)
            {
                if (active.Rage && active.RageTurns-- <= 0) { active.Rage = false; Emit("rageSpent", active); }
                if (sick && active.Status == "sick") Hurt(active, data.battleData.sickDamage);
                if (active.Hp > 0 && active.Status == "sleep")
                {
                    if (active.SleepFresh) active.SleepFresh = false;
                    else if (--active.SleepTurns <= 0) { active.Status = null; Emit("wake", active, "WOKE UP!"); }
                }
            }
            foreach (var mon in team.Where(mon => mon.Hp > 0))
                foreach (var id in mon.Cooldowns.Keys.ToArray()) mon.Cooldowns[id] = Math.Max(0, mon.Cooldowns[id] - 1);
        }

        private bool SwitchEnemy()
        {
            if (Enemy.Hp > 0) return false;
            Award(Enemy);
            int slot = Array.FindIndex(Opponents, mon => mon.Hp > 0);
            if (slot < 0) return false;
            EnemySlot = slot; Emit("switch", Enemy, Enemy.MonId.ToUpperInvariant()); return true;
        }

        private bool CheckEnd()
        {
            if (!Party.Any(mon => mon.Hp > 0)) Finish("enemy");
            else if (!Opponents.Any(mon => mon.Hp > 0)) Finish("player");
            return Outcome != null;
        }

        private void GainXp(Combatant mon, int value) { mon.Xp += value; Emit("xp", mon, amount: value); }
        private void Award(Combatant defeated)
        {
            if (!awarded.Add(defeated)) return;
            var team = participants.Where(mon => mon.Hp > 0).ToArray();
            if (team.Length == 0 && Player.Hp > 0) team = new[] {Player};
            if (team.Length == 0) return;
            int delta = defeated.Level - team.Max(mon => mon.Level);
            int xp = data.battleData.xpData.OrderBy(row => Math.Abs(row.delta - delta)).FirstOrDefault()?.xp ?? 10;
            foreach (var mon in team) GainXp(mon, xp);
            participants.Clear();
        }
        private void Finish(string outcome)
        {
            Outcome = outcome;
            if (outcome == "player") foreach (var foe in Opponents.Where(mon => mon.Hp <= 0)) Award(foe);
            if (outcome != "escaped" && Encounter.Kind == "training")
            {
                int xp = Math.Max(10, GameData.Round(Opponents.Sum(mon => mon.MaxHp - mon.Hp) / 18.0) + Math.Min(20, (Turn - 1) * 2));
                foreach (var mon in training) GainXp(mon, xp + (outcome == "player" ? 35 : 0));
            }
            Emit("end", text: outcome);
        }
    }
}
