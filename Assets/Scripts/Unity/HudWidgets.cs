using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Dynamons
{
    internal sealed class MonHudView
    {
        public string MonId, Status;
        public int Level, Hp, MaxHp;
        public bool Rage;

        public static MonHudView FromCombatant(Combatant mon, int? hpOverride = null) => new MonHudView
        {
            MonId = mon.MonId,
            Level = mon.Level,
            Hp = hpOverride ?? mon.Hp,
            MaxHp = mon.MaxHp,
            Status = mon.Status,
            Rage = mon.Rage,
        };

        public static MonHudView FromOwned(GameData data, OwnedMon mon) => new MonHudView
        {
            MonId = mon.MonId,
            Level = mon.Level,
            Hp = mon.Hp,
            MaxHp = data?.battleData?.maxHP ?? 100,
            Status = null,
            Rage = false,
        };
    }

    internal static class HudWidgets
    {
        private static string ValidKind(string kind) =>
            kind == "earth" || kind == "fire" || kind == "water" || kind == "electric" || kind == "dark" ? kind : "earth";

        public static void DrawClippedBar(GameUi ui, Transform parent, string bgPath, string fillPath,
            float x, float y, float w, float h, float ratio, float maskFrom = 0f, float maskTo = 1f, float alpha = 1f)
        {
            if (!string.IsNullOrEmpty(bgPath)) ui.Art(parent, bgPath, x, y, w, h, false, alpha);
            float amount = Mathf.Clamp01(ratio);
            if (amount <= 0f) return;
            DrawClippedSegment(ui, parent, fillPath, x, y, w, h, 0f, amount, maskFrom, maskTo, alpha);
        }

        public static void DrawClippedSegment(GameUi ui, Transform parent, string fillPath,
            float x, float y, float w, float h, float fromRatio, float toRatio, float maskFrom = 0f, float maskTo = 1f, float alpha = 1f)
        {
            float a = Mathf.Clamp01(fromRatio);
            float b = Mathf.Clamp01(toRatio);
            if (b <= a || string.IsNullOrEmpty(fillPath)) return;
            float startRel = w * (maskFrom + a * (maskTo - maskFrom));
            float width = w * (b - a) * (maskTo - maskFrom);
            if (width <= 0.01f) return;
            var clip = GameUi.Box("ClipMask", parent, x + startRel, y, width, h);
            clip.gameObject.AddComponent<RectMask2D>();
            ui.Art(clip, fillPath, -startRel, 0f, w, h, false, alpha);
        }

        public static Button DrawMuteButton(GameUi ui, Transform parent, SaveData save, string musicTrack,
            Action onSave = null, Action onMuteChanged = null)
        {
            bool muted = !save.Sound && !save.Music;
            const float w = 35f, h = 35f;
            float x = GameUi.Width - 5f - w;
            float y = 20f - h * 0.5f;
            return ui.ArtButton(parent, muted ? "general/btn_sound_off" : "general/btn_sound_on", x, y, w, h, () =>
            {
                bool nextMuted = !(!save.Sound && !save.Music);
                save.Sound = !nextMuted;
                save.Music = !nextMuted;
                ui.Music(musicTrack, save);
                onSave?.Invoke();
                onMuteChanged?.Invoke();
            }, true, 1f, "MuteButton");
        }

        public static void DrawTopHud(GameUi ui, BitmapFontRegistry fonts, GameData data, SaveData save,
            Transform parent, bool showMons = false)
        {
            float coinsX = GameUi.Width - 106f;
            float coinsY = 20f - 25f * 0.5f;
            ui.Art(parent, "general/hud_coins", coinsX, coinsY, 70f, 25f);
            fonts.Draw(parent, "white_stroked_small", (save?.Coins ?? 0).ToString(), coinsX + 41f, coinsY + 7f, 10f, "center");

            if (!showMons || data == null) return;
            float monsX = GameUi.Width - 194f;
            float monsY = 20f - 30f * 0.5f;
            ui.Art(parent, "team/hud_mons", monsX, monsY, 85f, 30f);
            var owned = new HashSet<string>((save?.Team ?? new List<OwnedMon>()).Select(m => m.MonId));
            var collectible = (data.mons ?? Array.Empty<MonData>()).Where(m => !m.bossOnly).ToArray();
            int uniqueOwned = collectible.Length > 0 ? collectible.Count(m => owned.Contains(m.id)) : owned.Count;
            int total = collectible.Length > 0 ? collectible.Length : 30;
            fonts.Draw(parent, "white_stroked_small", $"{uniqueOwned}/{total}", monsX + 57f, monsY + 9f, 10f, "center");
        }

        public static void DrawCaptainHud(GameUi ui, BitmapFontRegistry fonts, Transform parent,
            string enemyId, int totalMons, int remainingMons, float x, float y, string iconPathOverride = null)
        {
            const float iconW = 50f, iconH = 50f;
            string iconPath = string.IsNullOrEmpty(iconPathOverride) ? $"general/enemies/{enemyId}_icon" : iconPathOverride;
            ui.Art(parent, iconPath, x, y, iconW, iconH, true);
            fonts.Draw(parent, "white_stroked_big", (enemyId ?? string.Empty).ToUpperInvariant(), x + iconW + 6f, y + 4f, 15f, "left");
            int count = Math.Max(1, totalMons);
            int alive = Mathf.Clamp(remainingMons, 0, count);
            for (int i = 0; i < count; i++)
                ui.Art(parent, "session/ui/disk_icon", x + iconW + 5f + i * 24f, y + iconH - 25f, 22f, 22f, false, i < alive ? 1f : 0.3f);
        }

        public static void DrawMonPane(GameUi ui, BitmapFontRegistry fonts, GameData data, Transform parent,
            MonHudView mon, string type, float x, float y, float? trailRatio = null,
            bool showLevelUp = false, float alpha = 1f, bool showHpText = false, bool showHpBar = true)
        {
            if (mon == null) return;
            var info = data?.Mon(mon.MonId);
            string kind = ValidKind(info?.kind);
            int maxHp = mon.MaxHp > 0 ? mon.MaxHp : data?.battleData?.maxHP ?? 100;
            float ratio = Mathf.Clamp01(mon.Hp / (float)maxHp);
            string title = (info?.title ?? mon.MonId ?? string.Empty).ToUpperInvariant();

            if (type == "big")
            {
                const float w = 150f, h = 50f;
                ui.Art(parent, $"general/huds/pane_big_{kind}", x, y, w, h, false, alpha);
                float titleSize = 13f;
                float measured = fonts.Measure("white_stroked_big", title, titleSize);
                if (measured > 98f && measured > 0.001f) titleSize = Mathf.Max(8f, titleSize * 98f / measured);
                fonts.Draw(parent, "white_stroked_big", title, x + 7f, y + 11f, titleSize, "left");

                float lvlSize = mon.Level >= 100 ? 9.5f : 11f;
                float lvlX = mon.Level >= 100 ? x + w - 29f : x + w - 26f;
                fonts.Draw(parent, "black_medium", $"#{mon.Level}", lvlX, y + 12f, lvlSize, "center");

                float barX = x + (w - 140f) * 0.5f - 3f;
                float barY = y + h - 3f - 15f;
                ui.Art(parent, "general/huds/hpbar_big_bg", barX, barY, 140f, 15f, false, alpha);
                if (trailRatio.HasValue && trailRatio.Value > ratio)
                    DrawClippedSegment(ui, parent, "general/huds/hpbar_big_fill_red", barX, barY, 140f, 15f, ratio, trailRatio.Value, 0.025f, 0.97f, alpha);
                DrawClippedBar(ui, parent, null, ratio <= 0.25f ? "general/huds/hpbar_big_fill_red" : "general/huds/hpbar_big_fill",
                    barX, barY, 140f, 15f, ratio, 0.025f, 0.97f, alpha);

                if (showHpText)
                    fonts.Draw(parent, "white_stroked_small", $"{Mathf.RoundToInt(mon.Hp)}/{maxHp}", barX + 70f, barY + 4f, 7f, "center");

                if (mon.Status == "sick" || mon.Status == "hypno" || mon.Status == "sleep")
                    ui.Art(parent, $"session/ui/icon_{mon.Status}", x + 91f, y + 40f, mon.Status == "sick" ? 31f : 41f, 17f, true, alpha);
                if (mon.Rage)
                    ui.Art(parent, "session/ui/icon_rage", x + 56f, y + 40f, 34f, 17f, true, alpha);
                if (showLevelUp)
                    ui.Art(parent, "general/badge_levelup", x + w - 34f, y - 6f, 22f, 22f, false, alpha);
                return;
            }

            if (type == "thumb")
            {
                const float w = 120f, h = 65f;
                ui.Art(parent, $"general/huds/pane_thumb_{kind}", x, y, w, h, false, alpha);
                ui.Art(parent, GameUi.Mon(mon.MonId, "icon"), x + 7f, y + 6f, 48f, 48f, true, alpha);
                float titleSize = 9f;
                float measured = fonts.Measure("white_stroked_small", title, titleSize);
                if (measured > 96f && measured > 0.001f) titleSize = Mathf.Max(6.5f, titleSize * 96f / measured);
                fonts.Draw(parent, "white_stroked_small", title, x + w * 0.5f - 1f, y + 3f, titleSize, "center");
                fonts.Draw(parent, "black_medium", $"#{mon.Level}", x + 78f, y + 24f, 10f, "center");
                if (showHpBar)
                    DrawClippedBar(ui, parent, "general/huds/hpbar_small_bg", "general/huds/hpbar_small_fill",
                        x + w - 8f - 50f, y + h - 3f - 15f, 50f, 15f, ratio, 0.09f, 0.91f, alpha);
                string status = mon.Rage ? "rage" : mon.Status;
                if (!string.IsNullOrEmpty(status))
                    ui.Art(parent, $"session/ui/icon_{status}", x + 22f, y + h - 18f, 22f, 16f, true, alpha);
                if (showLevelUp)
                    ui.Art(parent, "general/badge_levelup", x + w - 26f, y - 6f, 22f, 22f, false, alpha);
                return;
            }

            if (type == "tooltip")
            {
                const float w = 100f, h = 65f;
                ui.Art(parent, $"general/huds/pane_tooltip_{kind}", x, y, w, h, false, alpha);
                float titleSize = 9f;
                float measured = fonts.Measure("white_stroked_small", title, titleSize);
                if (measured > 60f && measured > 0.001f) titleSize = Mathf.Max(6f, titleSize * 60f / measured);
                fonts.Draw(parent, "white_stroked_small", title, x + 7f, y + 21f, titleSize, "left");
                fonts.Draw(parent, "black_medium", $"#{mon.Level}", x + w - 17f, y + 21f, 9f, "center");
                if (showHpBar)
                    DrawClippedBar(ui, parent, "general/huds/hpbar_med_bg", "general/huds/hpbar_med_fill",
                        x + (w - 95f) * 0.5f + 1f, y + h - 8f - 20f, 95f, 20f, ratio, 0.04f, 0.96f, alpha);
                if (showLevelUp)
                    ui.Art(parent, "general/badge_levelup", x + w - 28f, y - 4f, 22f, 22f, false, alpha);
                return;
            }

            if (type == "mini")
            {
                const float w = 65f, h = 80f;
                ui.Art(parent, $"general/huds/pane_mini_{kind}", x, y, w, h, false, alpha);
                ui.Art(parent, GameUi.Mon(mon.MonId, "icon"), x + 5f, y + 6f, 55f, 55f, true, alpha);
                fonts.Draw(parent, "black_medium", $"#{mon.Level}", x + w * 0.5f, y + h - 16f, 10f, "center");
            }
        }

        public static float StatBarRatio(string stat, float val)
        {
            if (stat == "aim") return Mathf.Clamp01((Mathf.Pow(0.31f * val, 2f) * 0.1f + 2f) / 120f);
            return Mathf.Clamp01(val / 120f);
        }

        public static void DrawStatsPane(GameUi ui, BitmapFontRegistry fonts, Transform parent, Stats stats, float x, float y)
        {
            var rows = new[]
            {
                ("atk", "general/icon_atk", stats.atk),
                ("def", "general/icon_def", stats.def),
                ("aim", "general/icon_aim", stats.aim),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var (key, icon, val) = rows[i];
                float rowY = y + i * 33f;
                ui.Art(parent, icon, x, rowY, 75f, 30f);
                DrawClippedBar(ui, parent, "general/statsbar_bg", "general/statsbar_fill",
                    x + 22f, rowY + 16f, 100f, 15f, StatBarRatio(key, val));
                fonts.Draw(parent, "white_stroked_small", val.ToString(), x + 126f, rowY + 17f, 9f, "left");
            }
        }
    }
}
