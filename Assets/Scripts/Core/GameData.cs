using System;
using System.Linq;

namespace Dynamons
{
    [Serializable] public sealed class GameData
    {
        public MonData[] mons;
        public MoveData[] abilities;
        public ItemData[] items;
        public MapNode[] mapNodes;
        public MapPack[] mapPacks;
        public MapData mapData;
        public ArenaData arenaData;
        public LootData loot;
        public string shopMessage;
        public Product[] products;
        public BattleData battleData;
        public Tutorial tutorial;
        public Dialogue[] dialogs;
        public FontData[] fonts;

        public static bool HasPassive(Passive p) =>
            p != null && (!string.IsNullOrEmpty(p.id) || !string.IsNullOrEmpty(p.type) || !string.IsNullOrEmpty(p.label));

        public static string PassiveKey(Passive p)
        {
            if (!HasPassive(p)) return null;
            if (!string.IsNullOrEmpty(p.type)) return p.type;
            if (p.id == "shadow_phase" || p.chance > 0f) return "void";
            return null;
        }

        public static string PassiveTitle(Passive p) => PassiveKey(p) switch
        {
            "shield" => "SHELL SHIELD",
            "ramp" => "EMBER FURY",
            "charge" => "SPORE CHARGE",
            "void" => "VOID PHASE",
            _ => (p?.label ?? "PASSIVE").ToUpperInvariant(),
        };

        public static string PassiveDesc(Passive p)
        {
            if (!HasPassive(p)) return string.Empty;
            switch (PassiveKey(p))
            {
                case "shield":
                {
                    int hits = Math.Max(1, p.shieldHits);
                    return hits == 1
                        ? "Blocks 1st enemy attack each battle while awake."
                        : $"Blocks first {hits} enemy attacks while awake.";
                }
                case "ramp":
                {
                    int pct = Math.Max(1, Round(p.dmgPerStack * 100));
                    return $"Each attack builds BLAZE, raising DMG by {pct}% per stack.";
                }
                case "charge":
                {
                    int th = Math.Max(1, p.threshold);
                    string move = string.IsNullOrEmpty(p.boostedTitle) ? "SPORE SHOT" : p.boostedTitle.ToUpperInvariant();
                    return $"Charges in {th} hits into {move} (100% hit and Hypno).";
                }
                case "void":
                {
                    int ev = Round(p.chance * 100);
                    int pct = Math.Max(1, Round(p.dmgPerStack * 100));
                    return $"{ev}% chance to evade attacks and gain {pct}% DMG per evade.";
                }
                default:
                    return p.label ?? string.Empty;
            }
        }

        public void Normalize()
        {
            if (mons == null) return;
            foreach (var mon in mons)
                if (mon != null && !HasPassive(mon.passive)) mon.passive = null;
        }

        public MonData Mon(string id) => Array.Find(mons, row => row.id == id);
        public MoveData Move(string id) => Array.Find(abilities, row => row.id == id);
        public ItemData Item(string id) => Array.Find(items, row => row.id == id);
        public MapNode Node(int id) => Array.Find(mapNodes, row => row.id == id);
        public MapPack Pack(string id) => Array.Find(mapPacks, row => row.id == id);
        public Dialogue Dialog(string id) => Array.Find(dialogs, row => row.id == id);
        public FontData Font(string id) => Array.Find(fonts, row => row.id == id);
        public int MaxLevel => mons.Max(row => row.lvlTable.Length);

        public MoveData[] MovesAt(string id, int level)
        {
            var unlocked = Mon(id).abilities.Where(row => row.getsAt <= level).ToArray();
            var chosen = unlocked.GroupBy(row => row.index).Select(g => g.OrderBy(row => row.getsAt).Last()).ToList();
            foreach (var row in unlocked.Reverse().OrderByDescending(row => row.getsAt))
            {
                if (chosen.Count >= 5) break;
                if (chosen.All(move => move.id != row.id)) chosen.Add(row);
            }
            return chosen.OrderBy(row => row.index).ThenBy(row => row.getsAt).Take(5).Select(row => Move(row.id)).ToArray();
        }

        public static int Round(double value) => (int)Math.Floor(value + 0.5);
        public static int Clamp(int value, int lo, int hi) => Math.Max(lo, Math.Min(value, hi));

        public Stats StatsAt(string id, int level, bool extrapolate = false)
        {
            var table = Mon(id).lvlTable;
            int last = table.Length - 1;
            if (extrapolate && level > table.Length && last > 0)
            {
                int mid = last / 2;
                int extra = level - table.Length;
                int Grow(int a, int b, double min) => Round(a + Math.Max(min, (double)(a - b) / (last - mid)) * extra);
                var a = table[last]; var b = table[mid];
                return new Stats { atk = Grow(a.atk, b.atk, 1), def = Grow(a.def, b.def, 1), aim = Grow(a.aim, b.aim, .5), xp = a.xp };
            }
            int index = Clamp(level - 1, 0, last);
            var row = table[index];
            if (row.atk != 10 || row.def != 10 || row.aim != 10) return row;
            int real = Array.FindIndex(table, entry => entry.atk != 10 || entry.def != 10 || entry.aim != 10);
            if (real <= 0) return row;
            var first = table[real];
            int Lerp(int start, int end) => Round(start + (end - start) * (double)index / real);
            return new Stats { atk = Lerp(Round(first.atk * .44), first.atk), def = Lerp(Round(first.def * .44), first.def),
                aim = Lerp(Math.Max(42, Round(first.aim * .85)), first.aim), xp = row.xp };
        }

        public static bool Strong(string attack, string target) => attack switch
        {
            "fire" => target == "earth" || target == "dark", "earth" => target == "water" || target == "electric",
            "water" => target == "fire", "electric" => target == "water", "dark" => target == "earth" || target == "electric", _ => false
        };
        public float KindFactor(string attack, string target) => Strong(attack, target) ? battleData.kindFactors.strong :
            Strong(target, attack) ? battleData.kindFactors.weak : battleData.kindFactors.normal;
    }

    [Serializable] public struct Stats { public int atk, def, aim, xp; }
    [Serializable] public struct IntRange { public int min, max; }
    [Serializable] public class Named { public string id, title, desc; }
    [Serializable] public sealed class MonData : Named
    {
        public string kind;
        public bool bossOnly;
        public Stats[] lvlTable;
        public MoveSlot[] abilities;
        public CatchBand[] catchOddsRanges;
        public Passive passive;
    }
    [Serializable] public sealed class MoveSlot { public string id; public int index, getsAt; }
    [Serializable] public sealed class CatchBand { public int fromLvl; public CatchPoint[] data; }
    [Serializable] public struct CatchPoint { public float at, odds; }
    [Serializable] public sealed class Passive
    {
        public string id, label, type, abilityId, boostedTitle;
        public int threshold, shieldHits;
        public float chance, dmgPerStack;
    }
    [Serializable] public sealed class MoveData : Named
    {
        public string kind, effect;
        public int power, amount, cooldown;
        public float percent, chance;
        public VisualStep[] visualSteps;
    }
    [Serializable] public sealed class VisualStep { public string action, inHypno; public int at, duration; }
    [Serializable] public sealed class ItemData : Named { public bool inBattle; }
    [Serializable] public sealed class Choice { public string id, type; public int level; }
    [Serializable] public sealed class Tutorial { public Choice[] chooseMons; public int startingCoins; public Reward[] startingItems; }
    [Serializable] public sealed class Reward { public string id; public int amount; }
    [Serializable] public sealed class PoolRow { public string id, onlyFor; public float odds; public IntRange levels, amount; }
    [Serializable] public sealed class MapNode
    {
        public int id, connect;
        public int[] pos;
        public string pack, bg, unlocksPack;
        public bool arenaBattle;
        public DialogIds arenaDialogs;
        public PoolRow[] availMons, alsoAvailMons;
        public ScriptedBattle bossBattle, trainingBattle;
    }
    [Serializable] public class DialogIds { public string dialog_start, dialog_win, dialog_lose; }
    [Serializable] public sealed class ScriptedBattle : DialogIds
    {
        public string enemy, taunt, ai;
        public ScriptedMon[] mons;
        public Reward[] reward;
    }
    [Serializable] public sealed class ScriptedMon { public string id; public int level, maxHP; }
    [Serializable] public sealed class MapPack { public string id; public int advance; public PoolRow[] availMons; public Captains captains; }
    [Serializable] public sealed class Captains { public string[] avail; public IntRange levelsFactor; public WeightedAmount[] monsCount; }
    [Serializable] public sealed class WeightedAmount { public int amount; public float odds; }
    [Serializable] public class AdvanceRow { public int ifAdvancedTo; }
    [Serializable] public sealed class Visibility : AdvanceRow { public string[] visible; public int scrollMax; }
    [Serializable] public sealed class Ratios : AdvanceRow { public NodeOdds[] odds; }
    [Serializable] public sealed class NodeOdds { public string type; public float odds; }
    [Serializable] public sealed class Taunt { public string name; public string[] pool; }
    [Serializable] public sealed class MapData { public Visibility[] mapVisibility; public Ratios[] battleNodeRatios; public Taunt[] captainTaunts; }
    [Serializable] public sealed class ArenaData { public IntRange monLevelDeltas; public string[] taunts; public ArenaTeams[] monTeams; public ArenaRewards[] rewards; }
    [Serializable] public sealed class ArenaTeams : AdvanceRow { public MonTeam[] teams; }
    [Serializable] public sealed class ArenaRewards : AdvanceRow { public RewardBundle[] rewardsPool; }
    [Serializable] public sealed class MonTeam { public string[] mons; }
    [Serializable] public sealed class RewardBundle { public Reward[] rewards; }
    [Serializable] public sealed class LootData { public LootRow[] availLoot; public CoinRange[] wildAmounts, captainAmounts; }
    [Serializable] public sealed class LootRow : AdvanceRow { public PoolRow[] loot; }
    [Serializable] public sealed class CoinRange { public int distFromAdvance; public IntRange coins; }
    [Serializable] public sealed class Product : Named { public int price; public string lootTitle; public DisplayReward[] displayRewards; public LootStep[] loot; }
    [Serializable] public sealed class DisplayReward { public string icon, note; }
    [Serializable] public sealed class LootStep { public int amount; public PoolRow[] pool; }
    [Serializable] public sealed class BattleData
    {
        public int maxHP, sickDamage;
        public float hypnoBackfireChance, critFactor, rageFactor;
        public AimRow[] aiming;
        public string[] missableEffects;
        public VisualStep[] missVisualSteps;
        public KindFactors kindFactors;
        public XpRow[] xpData;
    }
    [Serializable] public sealed class AimRow { public int upTo; public float miss, crit; }
    [Serializable] public sealed class KindFactors { public float strong, weak, normal; }
    [Serializable] public sealed class XpRow { public int delta, xp; }
    [Serializable] public sealed class Dialogue { public string id; public DialogueLine[] content; }
    [Serializable] public sealed class DialogueLine { public string who, text; }
    [Serializable] public struct GlyphMetric { public int code, w, h, top, bottom; }
    [Serializable] public struct KernPair { public int left, right; public float adjust; }
    [Serializable] public sealed class FontData { public string id; public int capTop, baseline; public int[] chars; public GlyphMetric[] glyphs; public KernPair[] kerns; }
}
