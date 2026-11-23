using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    public static class SaveStore
    {
        public static SaveData Load(string path, GameData data)
        {
            return Read(path, data) ?? Read(path + ".bak", data) ?? SaveData.Create(data, DateTime.Today);
        }

        private static SaveData Read(string path, GameData data)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var save = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (save == null || save.Version != 1 || save.Event == null || save.Team == null || save.Inventory == null ||
                    save.SeenDialogs == null || save.ClearedNodes == null || save.UnlockedPacks == null ||
                    data.Node(save.NodeId) == null || !save.UnlockedPacks.Contains("a") || save.Coins < 0 || save.Event.Tickets < 0 ||
                    save.Event.NpcDrops < 0 || save.Event.NpcDrops > 3 || save.Event.Wins < 0 || string.IsNullOrEmpty(save.Event.Date) ||
                    save.Team.Any(mon => mon == null || string.IsNullOrEmpty(mon.Id) || data.Mon(mon.MonId) == null || mon.Level < 1 || mon.Level > data.MaxLevel || mon.Hp < 0 || mon.Hp > data.battleData.maxHP || mon.Xp < 0) ||
                    save.Team.Select(mon => mon.Id).Distinct().Count() != save.Team.Count ||
                    save.Inventory.Any(item => item == null || data.Item(item.id) == null || item.amount < 1)) return null;
                save.Speed = GameData.Clamp(save.Speed, 1, 3);
                return save;
            }
            catch (ArgumentException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static void Save(string path, SaveData save, GameData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(save));
            if (File.Exists(path))
                File.Replace(temporary, path, Read(path, data) != null ? path + ".bak" : null);
            else File.Move(temporary, path);
        }
    }
}
