using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamons
{
    public sealed class MemberSpec
    {
        public string MonId;
        public int Level, MaxHp, DamageCap, StatCap = 150;
        public bool Sparring;
        public Stats? Stats;
    }

    public sealed class Encounter
    {
        public string Kind = "none", Captain, Taunt, Background = "woods", Tier;
        public bool CanCapture, EventNpc;
        public MapNode Node;
        public DialogIds Dialogs;
        public SmartAi Ai;
        public MemberSpec[] Members = Array.Empty<MemberSpec>();
        public Reward[] Rewards = Array.Empty<Reward>();
    }

    public sealed class SmartAi
    {
        public int Heal, Antidote, Pepper, Berry;
        public double HealTrigger, PepperOdds;
    }

    public sealed class WorldRules
    {
        public static readonly string[] StarterRewards = { "charmander", "bulbasaur", "squirtle" };
        private readonly GameData data;
        private readonly Func<double> rng;
        public WorldRules(GameData data, Func<double> rng) { this.data = data; this.rng = rng; }

        public static T AtProgress<T>(T[] rows, int advance) where T : AdvanceRow => rows.LastOrDefault(row => advance >= row.ifAdvancedTo) ?? rows.FirstOrDefault();
        public IEnumerable<MapNode> Available(SaveData save) => data.mapNodes.Where(node => node.id == save.NodeId || save.UnlockedPacks.Contains(node.pack));
        public Visibility Visibility(SaveData save) => AtProgress(data.mapData.mapVisibility, save.Advance);
        private int Roll(IntRange range) => Math.Min(range.min, range.max) + (int)(rng() * (Math.Abs(range.max - range.min) + 1));
        private T Pick<T>(T[] rows) => rows == null || rows.Length == 0 ? default : rows[(int)(rng() * rows.Length)];
        private T Weight<T>(T[] rows, Func<T, double> weight) where T : class
        {
            if (rows == null) return null;
            double total = rows.Sum(row => Math.Max(0, weight(row)));
            if (total <= 0) return null;
            double remaining = rng() * total;
            foreach (var row in rows) if ((remaining -= Math.Max(0, weight(row))) < 0) return row;
            return rows.LastOrDefault(row => weight(row) > 0);
        }

        public Encounter StarterEvent() => new Encounter
        {
            Kind = "starter_event", Captain = "nyxalon", Background = "arena",
            Ai = new SmartAi { Heal = 1, Antidote = 1, Pepper = 1, HealTrigger = .35, PepperOdds = .5 },
            Members = new[] { new MemberSpec { MonId = "nyxalon", Level = 40, MaxHp = 120, StatCap = 200,
                Stats = new Stats { atk = 95, def = 30, aim = 200 } } },
        };

        public Encounter Training(string tier)
        {
            var node = data.mapNodes.First(x => x.trainingBattle?.mons?.Length > 0);
            var script = node.trainingBattle;
            if (tier != "hard" && tier != "normal") tier = "easy";
            bool hard = tier == "hard", easy = tier == "easy";
            int hp = hard ? 600 : easy ? 450 : 520, cap = hard ? 0 : easy ? 26 : 42;
            return new Encounter { Kind = "training", Tier = tier, Captain = script.enemy, Taunt = script.taunt, Node = node, Background = node.bg,
                Ai = new SmartAi { Heal = easy ? 2 : 3, Antidote = 1, Pepper = easy ? 1 : 2, Berry = 1,
                    HealTrigger = hard ? .5 : easy ? .38 : .45, PepperOdds = hard ? .85 : easy ? .35 : .65 },
                Members = script.mons.Select(row => new MemberSpec { MonId = row.id, Level = row.level, MaxHp = hp, Sparring = !hard, DamageCap = cap }).ToArray(),
                Rewards = script.reward.Select(row => new Reward { id = row.id, amount = row.amount + (row.id == "coins" ? hard ? 150 : easy ? 0 : 50 : 0) }).ToArray(),
            };
        }

        public Encounter Arena(SaveData save)
        {
            var node = data.mapNodes.First(n => n.arenaBattle);
            int advance = Math.Max(save.Advance, data.Pack(node.pack).advance);
            var table = data.arenaData;
            var mons = Pick(AtProgress(table.monTeams, advance).teams).mons;
            var levels = save.Team.Select(m => m.Level).OrderByDescending(x => x).Take(3).ToArray();
            int Level(int i) => levels.Length == 0 ? 3 + advance * 2 : levels[Math.Min(i, levels.Length - 1)];
            return new Encounter { Kind = "arena", Captain = "stephan", Background = "arena", Node = node, Taunt = Pick(table.taunts), Dialogs = node.arenaDialogs,
                Ai = new SmartAi { Heal = advance >= 4 ? 2 : 1, Antidote = 1, Pepper = 1, Berry = advance >= 3 ? 1 : 0, HealTrigger = .36, PepperOdds = .45 },
                Members = mons.Select((id, i) => new MemberSpec { MonId = id, Level = Math.Min(data.MaxLevel, Level(i) + Roll(table.monLevelDeltas)) }).ToArray(),
                Rewards = Pick(AtProgress(table.rewards, advance).rewardsPool).rewards };
        }

        public Encounter Captain(MapNode node, int advance)
        {
            var pack = data.Pack(node.pack);
            var source = node.availMons.Length > 0 ? node.availMons : pack.availMons.Concat(node.alsoAvailMons).ToArray();
            var pool = source.Where(row => string.IsNullOrEmpty(row.onlyFor) || row.onlyFor == "captain").ToArray();
            var info = pack.captains;
            if (info == null || pool.Length == 0) return new Encounter { Node = node };
            string captain = Pick(info.avail) ?? "kolin";
            int count = Math.Max(1, Weight(info.monsCount, row => row.odds)?.amount ?? 1);
            var members = new MemberSpec[count];
            for (int i = 0; i < count; i++)
            {
                var row = Weight(pool, x => x.odds) ?? pool[0];
                members[i] = new MemberSpec { MonId = row.id, Level = GameData.Clamp(Roll(row.levels) + Roll(info.levelsFactor), 1, data.MaxLevel) };
            }
            return new Encounter { Node = node, Kind = "captain", Captain = captain, Background = node.bg, Members = members,
                Ai = new SmartAi { Heal = advance >= 3 ? 1 : 0, Antidote = advance >= 2 ? 1 : 0, Pepper = advance >= 4 ? 1 : 0, Berry = advance >= 5 ? 1 : 0, HealTrigger = .32, PepperOdds = .35 },
                Taunt = Pick(data.mapData.captainTaunts.FirstOrDefault(row => row.name == captain)?.pool), Rewards = Rewards(advance, pack.advance, true) };
        }

        public Encounter Wild(MapNode node, int advance)
        {
            var pack = data.Pack(node.pack);
            var source = (node.availMons != null && node.availMons.Length > 0)
                ? node.availMons
                : (pack.availMons ?? Array.Empty<PoolRow>()).Concat(node.alsoAvailMons ?? Array.Empty<PoolRow>()).ToArray();
            var pool = source.Where(row => string.IsNullOrEmpty(row.onlyFor) || row.onlyFor == "wild").ToArray();
            var row = Weight(pool, m => m.odds) ?? pool.FirstOrDefault();
            if (row == null) return new Encounter { Node = node };
            return new Encounter { Node = node, Kind = "wild", CanCapture = true, Background = node.bg,
                Members = new[] { new MemberSpec { MonId = row.id, Level = Roll(row.levels) } }, Rewards = Rewards(advance, pack.advance, false) };
        }

        private Reward[] Rewards(int advance, int packAdvance, bool captain)
        {
            var rows = captain ? data.loot.captainAmounts : data.loot.wildAmounts;
            var coins = (rows.LastOrDefault(row => advance - packAdvance >= row.distFromAdvance) ?? rows[0]).coins;
            var list = new List<Reward> { new Reward { id = "coins", amount = Roll(coins) } };
            var loot = Weight(AtProgress(data.loot.availLoot, advance).loot, row => row.odds);
            if (loot != null) list.Add(new Reward { id = loot.id, amount = 1 });
            return list.ToArray();
        }

        public Dictionary<int, Encounter> RollMap(SaveData save)
        {
            var result = new Dictionary<int, Encounter>();
            var pool = new List<MapNode>();
            foreach (var node in Available(save))
            {
                if (node.trainingBattle?.mons?.Length > 0) result[node.id] = Training("easy");
                else if (node.arenaBattle) result[node.id] = Arena(save);
                else if (node.bossBattle?.mons?.Length > 0 && !save.ClearedNodes.Contains(node.id))
                {
                    var script = node.bossBattle;
                    result[node.id] = new Encounter { Node = node, Kind = "boss", Captain = script.enemy, Background = node.bg, Taunt = script.taunt,
                        Ai = new SmartAi { Heal = 1, Antidote = 1, Pepper = 1, Berry = 1, HealTrigger = .35, PepperOdds = .45 },
                        Dialogs = script, Rewards = script.reward, Members = script.mons.Select(m => new MemberSpec {MonId = m.id, Level = m.level}).ToArray() };
                }
                else if (node.id == save.NodeId) result[node.id] = new Encounter { Node = node };
                else pool.Add(node);
            }
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = (int)(rng() * (i + 1));
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var ratios = AtProgress(data.mapData.battleNodeRatios, save.Advance).odds;
            double total = ratios.Sum(x => x.odds);
            int Count(string kind) => total <= 0 ? 0 : GameData.Round(pool.Count * ratios.Where(x => x.type == kind).Sum(x => x.odds) / total);
            int wild = Math.Min(pool.Count, Math.Max(1, Count("wild"))), captains = Count("captain"), events = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var node = pool[i]; int advance = Math.Max(data.Pack(node.pack).advance, save.Advance);
                var encounter = i < wild ? Wild(node, advance) : i < wild + captains || events == 0 ? Captain(node, advance) : new Encounter {Node = node};
                if (encounter.Kind == "captain" && events < 3) { encounter.EventNpc = true; events++; }
                result[node.id] = encounter;
            }
            return result;
        }

        public int[] Path(SaveData save, int target)
        {
            var nodes = Available(save).ToArray();
            var queue = new Queue<int[]>(); var seen = new HashSet<int>();
            queue.Enqueue(new[] { save.NodeId }); seen.Add(save.NodeId);
            while (queue.Count != 0)
            {
                var path = queue.Dequeue(); int current = path[path.Length - 1];
                if (current == target) return path;
                int parent = data.Node(current).connect;
                foreach (var neighbor in nodes.Where(n => n.id == parent || n.connect == current))
                    if (seen.Add(neighbor.id)) queue.Enqueue(path.Concat(new[] { neighbor.id }).ToArray());
            }
            return Array.Empty<int>();
        }

        public Reward[] RollProduct(Product product)
        {
            var rewards = new List<Reward>();
            foreach (var step in product.loot) for (int i = 0; i < Math.Max(1, step.amount); i++)
            {
                var row = Weight(step.pool, it => it.odds);
                if (row != null) rewards.Add(new Reward { id = row.id, amount = row.id == "coins" ? Math.Max(row.amount.min, GameData.Round(Roll(row.amount) / 10.0) * 10) : 1 });
            }
            return rewards.ToArray();
        }
    }
}
