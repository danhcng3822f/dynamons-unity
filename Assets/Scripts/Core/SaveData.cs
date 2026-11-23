using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dynamons
{
    [Serializable] public sealed class OwnedMon
    {
        public string Id, MonId;
        public int Level, Hp, Xp;
        public static OwnedMon New(string id, int level, int hp) => new OwnedMon { Id = Guid.NewGuid().ToString("N"), MonId = id, Level = level, Hp = hp };
    }

    [Serializable] public sealed class EventSave
    {
        public string Date;
        public int Tickets, NpcDrops, Wins;
        public bool RewardPending;

        public bool Sync(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(key, Date) <= 0) return false;
            Date = key; NpcDrops = 0; Tickets++;
            return true;
        }

        public bool TryNpcDrop(DateTime date, Func<double> rng)
        {
            Sync(date);
            if (NpcDrops >= 3 || rng() >= .2) return false;
            NpcDrops++; Tickets++;
            return true;
        }
    }

    [Serializable] public sealed class SaveData
    {
        public int Version = 1, NodeId = 1, Advance = 1, Coins, ArenaWins, Speed = 1;
        public bool Sound = true, Music = true;
        public List<string> UnlockedPacks = new List<string> { "a" };
        public List<int> ClearedNodes = new List<int>();
        public List<string> SeenDialogs = new List<string>();
        public List<OwnedMon> Team = new List<OwnedMon>();
        public List<Reward> Inventory = new List<Reward>();
        public EventSave Event = new EventSave();

        public static SaveData Create(GameData data, DateTime date)
        {
            var save = new SaveData { Coins = data.tutorial.startingCoins };
            foreach (var reward in data.tutorial.startingItems) save.Add(reward.id, reward.amount);
            save.Event.Sync(date);
            return save;
        }

        public int Count(string id) => Inventory.Find(row => row.id == id)?.amount ?? 0;
        public bool Take(string id)
        {
            var row = Inventory.Find(it => it.id == id);
            if (row == null || row.amount < 1) return false;
            if (--row.amount == 0) Inventory.Remove(row);
            return true;
        }
        public void Add(string id, int amount)
        {
            if (id == "coins") { Coins += amount; return; }
            var row = Inventory.Find(it => it.id == id);
            if (row != null) row.amount += amount;
            else Inventory.Add(new Reward { id = id, amount = amount });
        }
    }
}
