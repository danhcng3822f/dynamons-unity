using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamons
{
    public sealed class GameFlow
    {
        private readonly Func<double> random;
        private BattleRules active;
        public GameData Data { get; }
        public SaveData Save { get; }
        public WorldRules World { get; }
        public GameFlow(GameData data, SaveData save, Func<double> random)
        { Data = data; Save = save; this.random = random; World = new WorldRules(data, random); }

        public bool ChooseStarter(string id)
        {
            var choice = Data.tutorial.chooseMons.FirstOrDefault(row => row.id == id);
            if (choice == null || Save.Team.Count != 0) return false;
            Save.Team.Add(OwnedMon.New(id, choice.level, Data.battleData.maxHP)); return true;
        }

        public bool MoveTo(int node)
        {
            if (World.Path(Save, node).Length == 0) return false;
            Save.NodeId = node; return true;
        }

        public OwnedMon[] LivingParty()
        {
            var activeParty = Save.Team.Take(3).Where(mon => mon.Hp > 0).ToArray();
            return activeParty.Length > 0 ? activeParty : Save.Team.Where(mon => mon.Hp > 0).Take(3).ToArray();
        }

        public BattleRules StartBattle(Encounter encounter, DateTime date)
        {
            if (active != null || encounter == null || encounter.Members.Length == 0 || Save.Event.RewardPending) return null;
            if (encounter.Node != null && (!World.Available(Save).Any(node => node.id == encounter.Node.id) || encounter.Node.id != Save.NodeId)) return null;
            var party = LivingParty();
            if (party.Length == 0) return null;
            bool ticket = encounter.Kind == "starter_event";
            if (ticket)
            {
                Save.Event.Sync(date);
                if (Save.Event.Tickets < 1) return null;
            }
            active = new BattleRules(Data, encounter, party, random);
            if (ticket) Save.Event.Tickets--;
            return active;
        }

        public void CancelEntry(BattleRules battle)
        {
            if (battle == null || active != battle || battle.Turn != 1 || battle.Outcome != null) return;
            if (battle.Encounter.Kind == "starter_event") Save.Event.Tickets++;
            active = null;
        }

        public Reward[] CommitBattle(BattleRules battle, DateTime date)
        {
            if (battle == null || active != battle || battle.Outcome == null) return Array.Empty<Reward>();
            active = null;
            foreach (var combatant in battle.Party)
            {
                var owned = Save.Team.Find(mon => mon.Id == combatant.Id);
                if (owned != null) { owned.Hp = combatant.Hp; owned.Xp = combatant.Xp; }
            }
            if (battle.Outcome != "player") return Array.Empty<Reward>();
            var encounter = battle.Encounter;
            if (encounter.Kind == "starter_event")
            {
                Save.Event.Wins++; Save.Event.RewardPending = true; return Array.Empty<Reward>();
            }
            var loot = new List<Reward>();
            if (battle.Caught != null) Save.Team.Add(OwnedMon.New(battle.Caught.MonId, battle.Caught.Level, Data.battleData.maxHP));
            else foreach (var reward in encounter.Rewards) { Save.Add(reward.id, reward.amount); loot.Add(reward); }
            if (encounter.EventNpc && Save.Event.TryNpcDrop(date, random)) loot.Add(new Reward { id = "starter_ticket", amount = 1 });
            if (encounter.Kind == "arena") Save.ArenaWins++;
            var node = encounter.Node;
            if (node != null && encounter.Kind != "training")
            {
                if (!Save.ClearedNodes.Contains(node.id)) Save.ClearedNodes.Add(node.id);
                if (!string.IsNullOrEmpty(node.pack) && !Save.UnlockedPacks.Contains(node.pack)) Save.UnlockedPacks.Add(node.pack);
                if (!string.IsNullOrEmpty(node.unlocksPack) && !Save.UnlockedPacks.Contains(node.unlocksPack)) Save.UnlockedPacks.Add(node.unlocksPack);
                Save.Advance = Math.Max(Save.Advance, Save.UnlockedPacks.Max(id => Data.Pack(id)?.advance ?? 1));
            }
            return loot.ToArray();
        }

        public bool ClaimReward(string mon)
        {
            if (!Save.Event.RewardPending || !WorldRules.StarterRewards.Contains(mon)) return false;
            Save.Team.Insert(Math.Min(Save.Team.Count, 3), OwnedMon.New(mon, 10, Data.battleData.maxHP));
            Save.Event.RewardPending = false; return true;
        }

        public Reward[] Buy(string id)
        {
            var product = Array.Find(Data.products, row => row.id == id);
            if (product == null || product.price <= 0 || Save.Coins < product.price) return Array.Empty<Reward>();
            var rewards = World.RollProduct(product);
            if (rewards.Length == 0) return rewards;
            Save.Coins -= product.price;
            foreach (var row in rewards) Save.Add(row.id, row.amount);
            return rewards;
        }

        public bool Swap(int a, int b)
        {
            if (!TeamIndex(a) || !TeamIndex(b) || a == b) return false;
            (Save.Team[a], Save.Team[b]) = (Save.Team[b], Save.Team[a]); return true;
        }
        public bool Release(int slot)
        {
            if (Save.Team.Count <= 1 || !TeamIndex(slot)) return false;
            Save.Team.RemoveAt(slot); return true;
        }
        private bool TeamIndex(int slot) => slot >= 0 && slot < Save.Team.Count;
        public void HealAll() { foreach (var mon in Save.Team) mon.Hp = Data.battleData.maxHP; }
        public int NeededXp(OwnedMon mon) => mon.Level >= Data.MaxLevel ? 0 : Data.StatsAt(mon.MonId, mon.Level + 1).xp;
        public bool LevelUp(int slot, string stone = null)
        {
            if (!TeamIndex(slot)) return false;
            var mon = Save.Team[slot]; int need = NeededXp(mon);
            if (need == 0 || mon.Xp < need) return false;
            string typeStone = "levelup_" + Data.Mon(mon.MonId).kind;
            stone ??= Save.Count(typeStone) > 0 ? typeStone : "levelup_joker";
            if ((stone != typeStone && stone != "levelup_joker") || !Save.Take(stone)) return false;
            mon.Level++; mon.Xp -= need; mon.Hp = Data.battleData.maxHP;
            return true;
        }
        public bool UseItem(string id, int slot = -1)
        {
            if (id == "healing_kit" && Save.Take(id)) { HealAll(); return true; }
            if (id == null || !id.StartsWith("levelup_", StringComparison.Ordinal)) return false;
            if (slot >= 0) return LevelUp(slot, id);
            for (int i = 0; i < Save.Team.Count; i++) if (LevelUp(i, id)) return true;
            return false;
        }
    }
}
