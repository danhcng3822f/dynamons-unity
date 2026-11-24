using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dynamons
{
    internal struct BattleBox
    {
        public float X, Y, W, H, Cx, Ground;
        public BattleBox(float x, float y, float w, float h, float cx, float ground)
        { X = x; Y = y; W = w; H = h; Cx = cx; Ground = ground; }
    }

    internal readonly struct EyeAnchor
    {
        public readonly float Rx, Ry, Scale;
        public EyeAnchor(float rx, float ry, float scale) { Rx = rx; Ry = ry; Scale = scale; }
    }

    internal static class BattleFxView
    {
        public static readonly IReadOnlyDictionary<string, (int cols, int rows)> FxSheets =
            new Dictionary<string, (int, int)>
            {
                { "slash", (1, 1) },
                { "blast", (4, 1) },
                { "blast_water", (4, 1) },
                { "rainbow", (4, 1) },
                { "splash", (4, 1) },
                { "splash_fire", (4, 1) },
                { "fire_spin", (5, 2) },
                { "enflame", (6, 1) },
                { "ice_shield", (5, 2) },
                { "rock_shield", (5, 2) },
                { "boulder_earth", (5, 1) },
                { "boulder_fire", (5, 1) },
                { "bubble", (4, 1) },
                { "smoke", (1, 1) },
                { "freeze_gas", (1, 1) },
                { "poison_gas", (1, 1) },
                { "spores_cloud", (1, 1) },
                { "heatwave", (1, 1) },
                { "winter", (1, 1) },
                { "wave", (1, 1) },
                { "rain_fire", (1, 1) },
                { "rain_ice", (1, 1) },
                { "rain_leaf", (1, 1) },
                { "rain_pods", (1, 1) },
                { "hypno_fx", (1, 1) },
                { "sick_fx", (5, 1) },
                { "spray", (1, 1) },
                { "vine_back", (1, 1) },
                { "vine_top", (1, 1) },
                { "tentacle_back", (1, 1) },
                { "tentacle_top", (1, 1) },
                { "can_open", (4, 1) },
                { "rage_fx_front", (4, 2) },
                { "rage_fx_back", (3, 1) },
            };

        public static readonly IReadOnlyDictionary<string, string> StepFxMap =
            new Dictionary<string, string>
            {
                { "fx-scratch", "slash" },
                { "fx-whip", "slash" },
                { "fx-fireblast", "blast" },
                { "fx-waterblast", "blast_water" },
                { "fx-rainbow", "rainbow" },
                { "fx-volcano", "splash_fire" },
                { "fx-icesplash", "splash" },
                { "fire_spin", "fire_spin" },
                { "enflame", "enflame" },
                { "fx-ice-shield", "ice_shield" },
                { "fx-earth-shield", "rock_shield" },
                { "boulders", "boulder_earth" },
                { "magma", "boulder_fire" },
                { "bubbles", "bubble" },
                { "fx-smoke", "smoke" },
                { "fx-freeze", "freeze_gas" },
                { "fx-poisongas", "poison_gas" },
                { "spore_clouds", "spores_cloud" },
                { "heatwave", "heatwave" },
                { "subzero", "winter" },
                { "sonic_waves", "wave" },
                { "fx-rain_fire", "rain_fire" },
                { "fx-rain_ice", "rain_ice" },
                { "fx-rain_leaf", "rain_leaf" },
                { "fx-rain_pods", "rain_pods" },
                { "confuse", "hypno_fx" },
                { "fx-rage", "rage_fx_front" },
                { "heal_spray", "spray" },
                { "antidote", "spray" },
            };

        public static readonly IReadOnlyDictionary<string, (EyeAnchor[] front, EyeAnchor[] back)> MegaEyeAnchors =
            new Dictionary<string, (EyeAnchor[], EyeAnchor[])>
            {
                { "enzation", (new[] { new EyeAnchor(0.155f, 0.452f, 1.05f), new EyeAnchor(0.311f, 0.449f, 1.05f) }, new[] { new EyeAnchor(0.902f, 0.448f, 0.95f) }) },
                { "pikachu", (new[] { new EyeAnchor(0.292f, 0.405f, 0.90f), new EyeAnchor(0.423f, 0.419f, 0.95f) }, new[] { new EyeAnchor(0.741f, 0.390f, 0.88f) }) },
                { "bulbasaur", (new[] { new EyeAnchor(0.114f, 0.579f, 0.95f), new EyeAnchor(0.374f, 0.584f, 1.00f) }, new[] { new EyeAnchor(0.931f, 0.522f, 0.85f) }) },
                { "charmander", (new[] { new EyeAnchor(0.125f, 0.375f, 0.90f), new EyeAnchor(0.301f, 0.348f, 1.00f) }, new[] { new EyeAnchor(0.813f, 0.310f, 0.90f) }) },
                { "squirtle", (new[] { new EyeAnchor(0.311f, 0.365f, 0.92f), new EyeAnchor(0.461f, 0.369f, 0.94f) }, new[] { new EyeAnchor(0.724f, 0.362f, 0.88f) }) },
                { "hyperbug", (new[] { new EyeAnchor(0.282f, 0.320f, 0.92f), new EyeAnchor(0.380f, 0.314f, 0.95f) }, new[] { new EyeAnchor(0.750f, 0.310f, 0.90f) }) },
                { "nyxalon", (new[] { new EyeAnchor(0.404f, 0.338f, 0.95f), new EyeAnchor(0.588f, 0.338f, 0.95f) }, new[] { new EyeAnchor(0.667f, 0.331f, 0.90f) }) },
            };

        private static readonly Color32[] RainbowColors =
        {
            new Color32(0xff, 0x2e, 0x4c, 0xff),
            new Color32(0xff, 0x8c, 0x00, 0xff),
            new Color32(0xff, 0xe6, 0x00, 0xff),
            new Color32(0x22, 0xe5, 0x5b, 0xff),
            new Color32(0x00, 0xd9, 0xff, 0xff),
            new Color32(0x3b, 0x6d, 0xff, 0xff),
            new Color32(0xb4, 0x3b, 0xff, 0xff),
        };

        private static readonly Dictionary<string, int[]> LevelupSequences = new Dictionary<string, int[]>
        {
            { "start", new[] { 0, 1, 2, 3, 4, 5, 6 } },
            { "blast", new[] { 7, 8, 9, 10, 11, 9, 10, 11, 9, 10, 11, 9, 10, 11 } },
            { "fade", new[] { 12, 13, 6, 5, 4 } },
        };

        private static string FxPath(string sheet) => $"session/abilities/fx/{sheet}";

        private static Vector2 Rot(Vector2 origin, float x, float y, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(origin.x + x * c - y * s, origin.y + x * s + y * c);
        }

        private static void DrawBolt(VectorCanvas vec, float x1, float y1, float x2, float y2, float seed, Color mid, int seg = 7, float jag = 14f, float alpha = 1f)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f || alpha <= 0f) return;
            var normal = new Vector2(-dy / len, dx / len);
            var pts = new Vector2[seg + 1];
            pts[0] = new Vector2(x1, y1);
            pts[seg] = new Vector2(x2, y2);
            for (int i = 1; i < seg; i++)
            {
                float t = i / (float)seg;
                float j = Mathf.Sin(seed * 12.7f + i * 7.3f) * jag;
                pts[i] = new Vector2(x1 + dx * t + normal.x * j, y1 + dy * t + normal.y * j);
            }
            vec.StrokePoly(pts, new Color(0f, 0.85f, 1f, alpha * 0.48f), 7f);
            vec.StrokePoly(pts, new Color(mid.r, mid.g, mid.b, alpha * 0.9f), 4f);
            vec.StrokePoly(pts, new Color(1f, 1f, 1f, alpha), 1.8f);
        }

        private static void DrawSheetFrame(GameUi ui, Transform fxLayer, string sheet, int cols, int rows, float progress,
            float x, float y, float w, float h, float alpha = 1f, float rotationDeg = 0f, bool alignBottom = true)
        {
            int idx = Mathf.Clamp(Mathf.FloorToInt(progress * cols * rows), 0, cols * rows - 1);
            ui.ArtCell(fxLayer, FxPath(sheet), cols, rows, idx, x, y, w, h, alpha, rotationDeg, alignBottom);
        }

        public static (float start, float dur) StepSpan(VisualStep step, float total)
        {
            string action = (step.action ?? string.Empty).Split(':')[0];
            float start = step.at;
            float dur = Mathf.Max(1f, step.duration);
            if (action == "boulders" || action == "magma") dur = Mathf.Max(dur, Mathf.Min(total - start, 1950f));
            else if (action == "confuse") dur = Mathf.Max(dur, Mathf.Min(total - start, 1500f));
            else if (action.StartsWith("fx-rain_", StringComparison.Ordinal)) dur = Mathf.Max(dur, Mathf.Min(total - start, 900f));
            else if (action == "fx-volcano" || action == "fx-icesplash" || action == "enflame") dur = Mathf.Max(dur, Mathf.Min(total - start, 650f));
            return (start, dur);
        }

        public static void RenderAbilityFx(Transform fxLayer, VectorCanvas vec, GameUi ui,
            string rawAction, float progress, BattleBox actorBox, BattleBox targetBox)
        {
            string action = (rawAction ?? string.Empty).Split(':')[0];
            float p = Mathf.Clamp01(progress);

            if (action == "flash")
            {
                float a = p < 0.15f ? p / 0.15f : Mathf.Max(0f, 1f - (p - 0.15f) / 0.85f);
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, new Color(1f, 1f, 1f, a * 0.8f));
                return;
            }

            if (action == "fx-spark")
            {
                float reach = Mathf.Min(1f, p * 1.5f);
                float fade = p > 0.72f ? (1f - p) / 0.28f : 1f;
                float sx = actorBox.Cx, sy = actorBox.Ground - 62f;
                float tx = targetBox.Cx, ty = targetBox.Ground - 62f;
                for (int i = 0; i < 3; i++)
                {
                    float ex = sx + (tx - sx) * reach + (i - 1) * 14f;
                    float ey = sy + (ty - sy) * reach + ((i % 2) * 16f - 8f);
                    DrawBolt(vec, sx, sy, ex, ey, i + Mathf.Floor(p * 10f) * 0.35f, GameUi.HexColor("#ffe63b"), 6, 12f, fade);
                }
                if (p >= 0.45f)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * (Mathf.PI / 3f) + p * 3f;
                        float r = 10f + (p - 0.45f) * 65f;
                        vec.StarFlare(tx + Mathf.Cos(ang) * r, ty + Mathf.Sin(ang) * r, 6f * fade, GameUi.HexColor("#fff55a", fade));
                    }
                }
                return;
            }

            if (action == "fx-volt_strike")
            {
                float fade = p < 0.15f ? p / 0.15f : p > 0.78f ? (1f - p) / 0.22f : 1f;
                float cx = targetBox.Cx, cy = targetBox.Ground - 66f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#00e5ff", Mathf.Sin(p * Mathf.PI) * 0.16f));
                for (int i = 0; i < 3; i++)
                {
                    float lp = Mathf.Clamp01(p * 1.4f - i * 0.12f);
                    if (lp <= 0f) continue;
                    float off = (i - 1) * 20f;
                    float dx = 48f * lp;
                    DrawBolt(vec, cx - 28f + off, cy - 38f + off * 0.4f, cx - 28f + off + dx * 1.2f, cy - 38f + off * 0.4f + dx * 1.15f,
                        5f + i + Mathf.Floor(p * 12f) * 0.4f, GameUi.HexColor("#fff55a"), 6, 10f, fade);
                }
                return;
            }

            if (action == "fx-static_charge")
            {
                float alpha = Mathf.Sin(p * Mathf.PI);
                float cx = actorBox.Cx, cy = actorBox.Ground - 60f;
                for (int i = 0; i < 3; i++)
                {
                    float rp = Mathf.Repeat(p * 1.8f + i / 3f, 1f);
                    vec.StrokeCircle(cx, cy, 18f + rp * 48f, GameUi.HexColor(i % 2 == 0 ? "#ffe63b" : "#00e5ff", (1f - rp) * alpha * 0.9f), 3f);
                }
                for (int i = 0; i < 4; i++)
                {
                    float a1 = p * 5f + i * (Mathf.PI * 0.5f), a2 = a1 + 0.95f;
                    DrawBolt(vec, cx + Mathf.Cos(a1) * 44f, cy + Mathf.Sin(a1) * 34f - p * 18f,
                        cx + Mathf.Cos(a2) * 48f, cy + Mathf.Sin(a2) * 38f - p * 18f, i + p * 6f, GameUi.HexColor("#ffe63b"), 5, 8f, alpha);
                }
                return;
            }

            if (action == "fx-shock_wave")
            {
                float fade = Mathf.Sin(p * Mathf.PI);
                float cx = targetBox.Cx, cy = targetBox.Ground - 62f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#191463", fade * 0.25f));
                for (int i = 0; i < 3; i++)
                {
                    float lp = Mathf.Clamp01(p * 1.4f - i * 0.16f);
                    if (lp <= 0f) continue;
                    vec.StrokeCircle(cx, cy, 14f + lp * 78f, GameUi.HexColor(i % 2 == 0 ? "#ffe63b" : "#00e5ff", (1f - lp) * 0.95f), 4f - lp * 2f);
                }
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * (Mathf.PI * 0.5f) + Mathf.PI * 0.25f + p * 1.4f;
                    DrawBolt(vec, cx, cy, cx + Mathf.Cos(ang) * 68f, cy + Mathf.Sin(ang) * 56f, i * 2 + Mathf.Floor(p * 10f), GameUi.HexColor("#ffe63b"), 5, 11f, fade);
                }
                return;
            }

            if (action == "fx-thunder_storm")
            {
                float fade = p < 0.1f ? p / 0.1f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#080c28", 0.36f * fade));
                var bolts = new[] { (ox: 0f, delay: 0f, jag: 20f, seed: 11f), (ox: -28f, delay: 0.08f, jag: 15f, seed: 23f), (ox: 26f, delay: 0.14f, jag: 16f, seed: 37f) };
                foreach (var b in bolts)
                {
                    float lp = Mathf.Clamp01((p - b.delay) / (1f - b.delay));
                    if (lp <= 0f) continue;
                    float strikeY = -10f + Mathf.Min(1f, lp * 3.8f) * (targetBox.Ground - 12f);
                    DrawBolt(vec, targetBox.Cx + b.ox * 0.45f, -10f, targetBox.Cx + b.ox, strikeY, b.seed + Mathf.Floor(p * 14f) * 0.3f, GameUi.HexColor("#fff55a"), 10, b.jag, fade);
                }
                if (p > 0.2f)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = -Mathf.PI + (i / 5f) * Mathf.PI;
                        float r = 10f + ((p - 0.2f) / 0.8f) * 52f;
                        vec.StarFlare(targetBox.Cx + Mathf.Cos(ang) * r, targetBox.Ground - 10f + Mathf.Sin(ang) * 14f, 7.5f * fade, GameUi.HexColor("#fff55a", fade));
                    }
                }
                return;
            }

            if (action == "fx-ion_overdrive")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                float beamP = Mathf.Clamp01(p / 0.55f);
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#04122e", 0.34f * fade));
                float sx = actorBox.Cx + 12f, sy = actorBox.Ground - 64f, tx = targetBox.Cx, ty = targetBox.Ground - 56f;
                float bx = sx + (tx - sx) * beamP, by = sy + (ty - sy) * beamP;
                float wMul = 0.85f + Mathf.Sin(p * 26f) * 0.18f;
                var line = new[] { new Vector2(sx, sy), new Vector2(bx, by) };
                vec.StrokePoly(line, GameUi.HexColor("#00d4ff", fade * 0.35f), 24f * wMul);
                vec.StrokePoly(line, GameUi.HexColor("#6ef3ff", fade * 0.72f), 13f * wMul);
                vec.StrokePoly(line, new Color(1f, 1f, 1f, fade * 0.96f), 5.5f * wMul);
                if (p > 0.25f)
                {
                    float ringP = (p - 0.25f) / 0.75f;
                    for (int i = 0; i < 3; i++)
                        vec.StrokeCircle(tx, ty, 14f + ringP * 48f + i * 11f, GameUi.HexColor(i % 2 == 0 ? "#00eaff" : "#ffe659", fade * (1f - ringP * 0.7f)), 2.6f);
                }
                for (int i = 0; i < 2; i++)
                    DrawBolt(vec, sx, sy, bx, by, i * 5 + Mathf.Floor(p * 16f) * 0.4f, GameUi.HexColor("#fff566"), 9, 15f, fade);
                if (p > 0.3f)
                {
                    vec.StarFlare(tx, ty, 18f * fade, GameUi.HexColor("#74f9ff", fade * 0.9f));
                    vec.StarFlare(tx, ty, 10f * fade, new Color(1f, 1f, 1f, fade));
                }
                return;
            }

            if (action == "fx-shadow_claw")
            {
                float fade = p < 0.15f ? p / 0.15f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                float slashP = Mathf.Clamp01(p / 0.68f);
                float tx = targetBox.Cx, ty = targetBox.Ground - 68f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#0c051c", 0.22f * fade));
                for (int i = -1; i <= 1; i++)
                {
                    float sx = tx - 38f, sy = ty + 24f + i * 16f;
                    var pts = new[] { new Vector2(sx, sy), new Vector2(sx + 78f * slashP, sy - 54f * slashP) };
                    vec.StrokePoly(pts, GameUi.HexColor("#49168c", fade * 0.55f), 9f);
                    vec.StrokePoly(pts, GameUi.HexColor("#d07aff", fade * 0.95f), 4.5f);
                    vec.StrokePoly(pts, new Color(1f, 1f, 1f, fade * 0.9f), 1.8f);
                }
                return;
            }

            if (action == "fx-night_pulse")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                float tx = targetBox.Cx, ty = targetBox.Ground - 62f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#080317", 0.34f * fade));
                for (int i = 0; i < 4; i++)
                {
                    float rp = Mathf.Clamp01(p * 1.4f - i * 0.14f);
                    if (rp <= 0f) continue;
                    vec.StrokeCircle(tx, ty, 12f + rp * 54f, GameUi.HexColor(i % 2 == 0 ? "#a852ff" : "#e6b8ff", fade * (1f - rp * 0.65f)), 4f - rp * 2f);
                }
                vec.FillCircle(tx, ty, 16f * Mathf.Sin(p * Mathf.PI), GameUi.HexColor("#360c70", fade * 0.88f));
                return;
            }

            if (action == "fx-gloom_veil")
            {
                float fade = p < 0.15f ? p / 0.15f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                float cx = actorBox.Cx, cy = actorBox.Ground - 58f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#120729", 0.2f * fade));
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * (Mathf.PI / 3f) + p * 6.5f;
                    float rx = 28f + Mathf.Sin(p * Mathf.PI) * 14f;
                    float px = cx + Mathf.Cos(ang) * rx, py = cy + Mathf.Sin(ang) * 16f - p * 34f;
                    vec.StrokePoly(new[] { new Vector2(px - 6f, py + 8f), new Vector2(px, py - 8f) },
                        GameUi.HexColor(i % 2 == 0 ? "#c37dff" : "#6f29cf", fade * 0.9f), 3.2f);
                }
                return;
            }

            if (action == "fx-umbral_slash")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                float tx = targetBox.Cx, ty = targetBox.Ground - 66f;
                float cutP = Mathf.Clamp01(p / 0.6f);
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#050210", 0.44f * fade));
                var cuts = new[]
                {
                    new[] { new Vector2(tx - 46f, ty - 42f), new Vector2(tx + 46f * cutP, ty + 42f * cutP) },
                    new[] { new Vector2(tx + 46f, ty - 42f), new Vector2(tx - 46f * cutP, ty + 42f * cutP) },
                };
                foreach (var cut in cuts)
                {
                    vec.StrokePoly(cut, GameUi.HexColor("#5917ad", fade * 0.55f), 12f);
                    vec.StrokePoly(cut, GameUi.HexColor("#d896ff", fade * 0.95f), 5.5f);
                    vec.StrokePoly(cut, new Color(1f, 1f, 1f, fade * 0.95f), 2.2f);
                }
                if (p > 0.28f) vec.StarFlare(tx, ty, 17f * fade, GameUi.HexColor("#eed1ff", fade));
                return;
            }

            if (action == "fx-slumber_hex")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                float tx = targetBox.Cx, ty = targetBox.Ground - 68f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#0f0629", 0.36f * fade));
                float rad = 26f + Mathf.Sin(p * Mathf.PI) * 14f;
                float rot = p * Mathf.PI * 1.6f;
                var tri = new Vector2[3];
                for (int i = 0; i < 3; i++)
                {
                    float a = i * (Mathf.PI * 2f / 3f) - Mathf.PI * 0.5f + rot;
                    tri[i] = new Vector2(tx + Mathf.Cos(a) * rad, ty + Mathf.Sin(a) * rad);
                }
                vec.StrokePoly(tri, GameUi.HexColor("#d38eff", fade * 0.88f), 3f, true);
                vec.StrokeCircle(tx, ty, 38f + Mathf.Sin(p * Mathf.PI) * 10f, GameUi.HexColor("#9747ff", fade * 0.72f), 2.6f);
                for (int i = 0; i < 3; i++)
                {
                    float zp = Mathf.Clamp01(p * 1.4f - i * 0.2f);
                    if (zp <= 0f) continue;
                    float zx = tx + (i - 1) * 20f, zy = ty + 6f - zp * 46f, s = 5f + i * 1.4f;
                    vec.StrokePoly(new[] { new Vector2(zx - s, zy - s), new Vector2(zx + s, zy - s), new Vector2(zx - s, zy + s), new Vector2(zx + s, zy + s) },
                        GameUi.HexColor("#ffe99c", fade * Mathf.Sin(zp * Mathf.PI)), 2.4f);
                }
                return;
            }

            if (action == "fx-eclipse_nova")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                float tx = targetBox.Cx, ty = targetBox.Ground - 66f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#080216", 0.42f * fade));
                for (int i = 0; i < 3; i++)
                    vec.StrokeCircle(tx, ty, 18f + i * 14f + Mathf.Sin(p * Mathf.PI) * 16f,
                        GameUi.HexColor(i == 0 ? "#fde047" : "#d946ef", fade * (0.9f - i * 0.22f)), 4f - i);
                vec.StarFlare(tx, ty, 22f * fade, GameUi.HexColor("#fff08a", fade));
                return;
            }

            if (action == "fx-volt_tackle")
            {
                float fade = p < 0.1f ? p / 0.1f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#1b1400", 0.24f * fade));
                float sx = actorBox.Cx, sy = actorBox.Ground - 52f, tx = targetBox.Cx, ty = targetBox.Ground - 54f;
                float rush = Mathf.Clamp01(p * 1.9f);
                float px = sx + (tx - sx) * rush, py = sy + (ty - sy) * rush;
                for (int i = -2; i <= 2; i++)
                {
                    vec.StrokePoly(new[] { new Vector2(sx + i * 7f, sy + i * 6f), new Vector2(px + i * 7f, py + i * 6f) },
                        GameUi.HexColor(i % 2 == 0 ? "#ffe135" : "#2ee8ff", fade * (i == 0 ? 0.95f : 0.65f)), i == 0 ? 7f : 3f);
                }
                if (p > 0.35f)
                {
                    float boomP = (p - 0.35f) / 0.65f;
                    vec.FillCircle(tx, ty, 14f + boomP * 34f, GameUi.HexColor("#ffe94f", fade * (1f - boomP * 0.55f)));
                }
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * (Mathf.PI * 0.5f) + p * 7f;
                    float rad = 18f + Mathf.Sin(p * Mathf.PI) * 34f;
                    DrawBolt(vec, tx, ty, tx + Mathf.Cos(ang) * rad, ty + Mathf.Sin(ang) * rad, i * 3 + Mathf.Floor(p * 14f), GameUi.HexColor("#fff24a"), 5, 11f, fade);
                }
                if (p > 0.4f)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float ang = i * (Mathf.PI * 2f / 5f) + p * 3f;
                        vec.StarFlare(tx + Mathf.Cos(ang) * 28f, ty + Mathf.Sin(ang) * 24f, 7f * fade, GameUi.HexColor("#fff566", fade));
                    }
                }
                return;
            }

            if (action == "fx-solar_bloom")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#0c2906", 0.26f * fade));
                float tx = targetBox.Cx, ty = targetBox.Ground - 54f;
                vec.FillPoly(new[]
                {
                    new Vector2(tx - 64f, -10f), new Vector2(tx - 10f, -10f),
                    new Vector2(tx + 34f, targetBox.Ground + 4f), new Vector2(tx - 34f, targetBox.Ground + 4f),
                }, GameUi.HexColor("#fff375", fade * 0.42f));
                float bloomR = 14f + Mathf.Sin(p * Mathf.PI) * 38f;
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * (Mathf.PI / 3f) + p * 2.2f;
                    vec.StrokePoly(new[] { new Vector2(tx, ty), new Vector2(tx + Mathf.Cos(ang) * bloomR, ty + Mathf.Sin(ang) * bloomR * 0.82f) },
                        GameUi.HexColor(i % 2 == 0 ? "#7eff5e" : "#ffe954", fade * 0.85f), 4.2f);
                }
                vec.StrokeCircle(actorBox.Cx, actorBox.Ground - 46f, 14f + p * 26f, GameUi.HexColor("#9dff6b", fade * 0.8f), 3f);
                vec.StarFlare(tx, ty, 13f * fade, GameUi.HexColor("#fff899", fade));
                vec.StarFlare(actorBox.Cx, actorBox.Ground - 48f, 9f * fade, GameUi.HexColor("#6dff72", fade));
                return;
            }

            if (action == "fx-flare_burst")
            {
                float fade = p < 0.1f ? p / 0.1f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#2b0900", 0.28f * fade));
                float sx = actorBox.Cx + 8f, sy = actorBox.Ground - 56f, tx = targetBox.Cx, ty = targetBox.Ground - 52f;
                float travel = Mathf.Clamp01(p * 2.1f);
                float bx = sx + (tx - sx) * travel, by = sy + (ty - sy) * travel - Mathf.Sin(travel * Mathf.PI) * 28f;
                float coreR = p < 0.45f ? 11f : 12f + ((p - 0.45f) / 0.55f) * 40f;
                vec.FillCircle(bx, by, coreR, GameUi.HexColor("#ff5a14", fade * 0.72f));
                vec.FillCircle(bx, by, coreR * 0.55f, GameUi.HexColor("#ffd942", fade * 0.72f));
                float spread = p < 0.42f ? 16f : 22f + ((p - 0.42f) / 0.58f) * 44f;
                for (int i = 0; i < 7; i++)
                {
                    float ang = i * (Mathf.PI * 2f / 7f) + p * 3.5f;
                    vec.StrokePoly(new[] { new Vector2(bx, by), new Vector2(bx + Mathf.Cos(ang) * spread, by + Mathf.Sin(ang) * spread) },
                        GameUi.HexColor(i % 2 == 0 ? "#ff6f17" : "#ffe14d", fade * 0.9f), 4f);
                }
                vec.StarFlare(bx, by, 14f * fade, GameUi.HexColor("#fff4a6", fade));
                return;
            }

            if (action == "fx-hydro_vortex")
            {
                float fade = p < 0.12f ? p / 0.12f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#031f3d", 0.28f * fade));
                float tx = targetBox.Cx, ty = targetBox.Ground - 52f;
                for (int rIdx = 0; rIdx < 3; rIdx++)
                {
                    float r = 16f + rIdx * 13f + Mathf.Sin(p * Mathf.PI) * 14f;
                    float startA = p * 8f + rIdx * 1.6f;
                    Color arcColor = rIdx == 0 ? Color.white : GameUi.HexColor(rIdx == 1 ? "#3de8ff" : "#0088ff");
                    arcColor.a = fade * (0.95f - rIdx * 0.15f);
                    vec.StrokeCircle(tx, ty, r, arcColor, 4.6f - rIdx * 0.8f, startA, startA + Mathf.PI * 1.35f, 18);
                }
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * (Mathf.PI / 3f) + p * 5f;
                    float outer = 26f + p * 34f;
                    vec.StrokePoly(new[] { new Vector2(tx + Mathf.Cos(ang) * 12f, ty + Mathf.Sin(ang) * 12f), new Vector2(tx + Mathf.Cos(ang) * outer, ty + Mathf.Sin(ang) * outer) },
                        i % 2 == 0 ? GameUi.HexColor("#7af2ff", fade * 0.85f) : new Color(1f, 1f, 1f, fade * 0.85f), 2.8f);
                }
                vec.StarFlare(tx, ty, 11f * fade, GameUi.HexColor("#b8fbff", fade));
                return;
            }

            if (action == "fx-water_cannon")
            {
                float fade = p < 0.1f ? p / 0.1f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#042447", 0.24f * fade));
                float tx = targetBox.Cx, ty = targetBox.Ground - 52f;
                float ax = actorBox.Cx + (tx >= actorBox.Cx ? 18f : -18f), ay = actorBox.Ground - 60f;
                float dx = tx - ax, dy = ty - ay;
                float dist = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dy * dy));
                float ang = Mathf.Atan2(dy, dx);
                float reach = Mathf.Clamp(p * 1.45f, 0.15f, 1f) * dist;
                vec.StrokeCircle(ax, ay, 8f + Mathf.Sin(p * Mathf.PI) * 8f, GameUi.HexColor("#7bf4ff", fade * 0.88f), 2.8f);

                var origin = new Vector2(ax, ay);
                var beamEnd = Rot(origin, reach, 0f, ang);
                var beamStartCore = Rot(origin, 4f, 0f, ang);
                vec.StrokePoly(new[] { origin, beamEnd }, GameUi.HexColor("#0095ff", fade * 0.88f), 22f - p * 4f);
                vec.StrokePoly(new[] { origin, beamEnd }, GameUi.HexColor("#5cf0ff", fade * 0.88f), 12f);
                vec.StrokePoly(new[] { beamStartCore, beamEnd }, new Color(1f, 1f, 1f, fade * 0.88f), 5f);

                for (int rIdx = 0; rIdx < 3; rIdx++)
                {
                    float frac = Mathf.Repeat(p * 1.8f + rIdx * 0.3f, 1f);
                    float rx = frac * reach, ryRad = 13f + frac * 6f;
                    var ringPts = new Vector2[16];
                    for (int k = 0; k < 16; k++)
                    {
                        float theta = k * (Mathf.PI * 2f / 16f);
                        ringPts[k] = Rot(origin, rx + Mathf.Cos(theta) * 4.5f, Mathf.Sin(theta) * ryRad, ang);
                    }
                    Color rc = rIdx % 2 == 0 ? new Color(1f, 1f, 1f, fade * (1f - frac * 0.45f)) : GameUi.HexColor("#6befff", fade * (1f - frac * 0.45f));
                    vec.StrokePoly(ringPts, rc, 2.6f, true);
                }

                float impact = Mathf.Clamp01((p - 0.18f) / 0.82f);
                if (impact > 0f)
                {
                    vec.StrokeCircle(tx, ty, 12f + impact * 34f, GameUi.HexColor("#8ef8ff", fade * (1f - impact * 0.6f)), 3.2f);
                    for (int i = 0; i < 8; i++)
                    {
                        float ray = i * (Mathf.PI * 0.25f) + p * 2.4f;
                        float r0 = 8f + impact * 6f, r1 = 22f + impact * 30f;
                        vec.StrokePoly(new[] { new Vector2(tx + Mathf.Cos(ray) * r0, ty + Mathf.Sin(ray) * r0), new Vector2(tx + Mathf.Cos(ray) * r1, ty + Mathf.Sin(ray) * r1) },
                            i % 2 == 0 ? new Color(1f, 1f, 1f, fade * 0.92f) : GameUi.HexColor("#3ee7ff", fade * 0.92f), 2.6f);
                    }
                    vec.StarFlare(tx, ty, 12f * fade, GameUi.HexColor("#d8feff", fade));
                }
                return;
            }

            if (action == "fx-fireblast" || action == "fx-waterblast" || action == "fx-rainbow")
            {
                string sheet = StepFxMap[action];
                const float w = 148f, h = 475f;
                float alpha = p < 0.12f ? p / 0.12f : p > 0.85f ? (1f - p) / 0.15f : 1f;
                DrawSheetFrame(ui, fxLayer, sheet, 4, 1, Mathf.Repeat(p * 2.2f, 1f), targetBox.Cx - w * 0.5f, targetBox.Ground + 16f - h, w, h, alpha);
                return;
            }

            if (action == "heatwave" || action == "subzero")
            {
                float alpha = Mathf.Sin(p * Mathf.PI) * 0.45f;
                vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor(action == "heatwave" ? "#d33b0e" : "#aefeff", alpha));
                float sweepX = -160f + p * (GameUi.Width + 280f);
                ui.Art(fxLayer, FxPath(action == "heatwave" ? "heatwave" : "winter"), sweepX, 90f, 260f, 240f, true, Mathf.Clamp01(alpha * 1.4f));
                return;
            }

            if (action.StartsWith("fx-rain_", StringComparison.Ordinal))
            {
                string fxPath = FxPath(action.Substring(3));
                bool ltr = actorBox.Cx < GameUi.Width * 0.5f;
                for (int i = 0; i < 12; i++)
                {
                    float localP = Mathf.Clamp01(p * 1.65f - i * 0.055f);
                    if (localP <= 0f || localP >= 1f) continue;
                    float spreadX = ((i % 4) - 1.5f) * 26f + ((i % 2) != 0 ? 7f : -7f);
                    float endX = targetBox.Cx + spreadX, endY = targetBox.Ground - 12f + (i % 3) * 10f;
                    float startX = endX + (ltr ? -92f : 92f), startY = -44f - (i / 4) * 22f;
                    float px = startX + (endX - startX) * localP, py = startY + (endY - startY) * localP;
                    float alpha = Mathf.Sin(localP * Mathf.PI);
                    float angleDeg = (Mathf.Atan2(endY - startY, endX - startX) - Mathf.PI * 0.5f) * Mathf.Rad2Deg;
                    ui.ArtCell(fxLayer, fxPath, 1, 1, 0, px - 16f, py - 22f, 32f, 44f, alpha, angleDeg);
                }
                return;
            }

            if (action == "sonic_waves")
            {
                string fxPath = FxPath("wave");
                for (int i = 0; i < 5; i++)
                {
                    float localP = Mathf.Clamp01(p * 1.45f - i * 0.14f);
                    if (localP <= 0f) continue;
                    float x = Mathf.Lerp(actorBox.Cx, targetBox.Cx, localP);
                    float y = Mathf.Lerp(actorBox.Ground - 55f, targetBox.Ground - 55f, localP);
                    float size = 28f + localP * 54f;
                    ui.Art(fxLayer, fxPath, x - size * 0.5f, y - size * 0.5f, size, size, true, (1f - localP * 0.65f) * 0.9f);
                }
                return;
            }

            if (action == "fx-scratch" || action == "fx-whip")
            {
                string fxPath = FxPath("slash");
                int slashes = action == "fx-whip" ? 5 : 3;
                for (int i = 0; i < slashes; i++)
                {
                    float localP = Mathf.Clamp01(p * 1.45f - i * 0.11f);
                    if (localP <= 0f) continue;
                    float ox = (i - (slashes - 1) * 0.5f) * 14f, oy = (i - (slashes - 1) * 0.5f) * 11f;
                    float rotRad = action == "fx-whip" ? ((i - 2) * 28f * Mathf.PI) / 180f : (-45f * Mathf.PI) / 180f;
                    float alpha = Mathf.Sin(localP * Mathf.PI);
                    float slide = (localP - 0.5f) * 26f;
                    float sx = targetBox.Cx + ox + Mathf.Cos(rotRad) * slide, sy = targetBox.Ground - 68f + oy + Mathf.Sin(rotRad) * slide;
                    ui.ArtCell(fxLayer, fxPath, 1, 1, 0, sx - 56f, sy - 7f, 112f, 14f, alpha, rotRad * Mathf.Rad2Deg);
                }
                return;
            }

            if (action == "fx-smoke" || action == "fx-freeze" || action == "fx-poisongas" || action == "spore_clouds")
            {
                string fxPath = FxPath(StepFxMap[action]);
                float dist = 10f + p * 26f, size = 48f + p * 34f, alpha = Mathf.Sin(p * Mathf.PI) * 0.78f;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * (Mathf.PI * 0.5f) + p * 0.8f;
                    ui.Art(fxLayer, fxPath, targetBox.Cx + Mathf.Cos(angle) * dist - size * 0.5f,
                        targetBox.Y + 44f + Mathf.Sin(angle) * dist * 0.6f - size * 0.5f, size, size, true, alpha);
                }
                return;
            }

            if (action == "boulders" || action == "magma")
            {
                string sheet = action == "boulders" ? "boulder_earth" : "boulder_fire";
                var specs = new[] { (ox: 28f, oy: 0f, delay: 0f), (ox: -28f, oy: -4f, delay: 0.05f), (ox: 0f, oy: -28f, delay: 0.1f) };
                foreach (var sp in specs)
                {
                    float lp = Mathf.Clamp01((p - sp.delay) / (1f - sp.delay));
                    if (lp <= 0f) continue;
                    float by, shakeX = 0f, alpha = 1f;
                    int frameIdx = 0;
                    float landY = targetBox.Ground - 110f + sp.oy;
                    if (lp < 0.28f) { float drop = lp / 0.28f; by = -120f + (landY + 120f) * (drop * drop); }
                    else if (lp < 0.66f) { by = landY; if (lp > 0.42f) shakeX = Mathf.Sin((lp - 0.42f) * 140f) * 6f; }
                    else { by = landY; float shatter = (lp - 0.66f) / 0.34f; frameIdx = 1 + Mathf.Clamp(Mathf.FloorToInt(shatter * 4f), 0, 3); alpha = 1f - Mathf.Max(0f, (shatter - 0.7f) / 0.3f); }
                    ui.ArtCell(fxLayer, FxPath(sheet), 5, 1, frameIdx, targetBox.Cx + sp.ox - 58f + shakeX, by, 116f, 96f, alpha, 0f, true);
                }
                return;
            }

            if (action == "bubbles")
            {
                for (int i = 0; i < 4; i++)
                {
                    float localP = Mathf.Clamp01(p * 1.3f - i * 0.1f);
                    if (localP <= 0f) continue;
                    float bx = targetBox.Cx - 38f + (i % 2) * 34f + Mathf.Sin(localP * 6f + i) * 8f;
                    float by = targetBox.Ground - 28f - localP * 85f;
                    DrawSheetFrame(ui, fxLayer, "bubble", 4, 1, localP, bx, by, 46f, 46f, 0.9f);
                }
                return;
            }

            if (action == "fx-rage")
            {
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt(p * 8f), 0, 7);
                float alpha = p < 0.12f ? p / 0.12f : p > 0.85f ? (1f - p) / 0.15f : 0.95f;
                ui.ArtCell(fxLayer, FxPath("rage_fx_back"), 3, 1, frameIdx % 3, actorBox.Cx - 66f, actorBox.Ground - 134f, 132f, 134f, alpha * 0.8f, 0f, true);
                ui.ArtCell(fxLayer, FxPath("rage_fx_front"), 4, 2, frameIdx, actorBox.Cx - 72f, actorBox.Ground - 146f, 144f, 146f, alpha, 0f, true);
                return;
            }

            if (action == "confuse")
            {
                float alpha = Mathf.Sin(p * Mathf.PI) * 0.88f;
                float size = 34f + Mathf.Sin(p * Mathf.PI) * 10f;
                ui.ArtCell(fxLayer, FxPath("hypno_fx"), 1, 1, 0, targetBox.Cx - size * 0.5f, targetBox.Y + 24f - size * 0.5f, size, size, alpha, p * 540f);
                return;
            }

            if (action == "revive")
            {
                string fxPath = FxPath("wave");
                for (int i = 0; i < 4; i++)
                {
                    float localP = Mathf.Clamp01(p * 1.6f - i * 0.2f);
                    if (localP <= 0f || localP >= 1f) continue;
                    float size = (1f - localP * 0.9f) * 110f;
                    ui.Art(fxLayer, fxPath, actorBox.Cx - size * 0.5f, actorBox.Ground - 68f - size * 0.5f, size, size, true, Mathf.Sin(localP * Mathf.PI) * 0.92f);
                }
                return;
            }

            if (action == "grip_vine" || action == "grip_tentacle")
            {
                string kind = action == "grip_vine" ? "vine" : "tentacle";
                string backPath = FxPath(kind + "_back"), topPath = FxPath(kind + "_top");
                float enter = p < 0.22f ? p / 0.22f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                float squeeze = 1f - Mathf.Sin(Mathf.Clamp01((p - 0.18f) / 0.62f) * Mathf.PI) * 0.18f;
                float spread = (1f - enter) * 58f;
                var strands = new[] { (-14f - spread, -48f, 0.17f), (14f + spread, -66f, -0.22f), (-10f - spread, -30f, -0.09f), (10f + spread, -82f, 0.11f) };
                foreach (var (ox, oy, rot) in strands)
                    ui.ArtCell(fxLayer, backPath, 1, 1, 0, targetBox.Cx + ox - 78f, targetBox.Ground + oy - 5f, 156f, 10f, 0.92f * enter, rot * Mathf.Rad2Deg);
                if (p >= 0.14f)
                {
                    float topW = 128f * squeeze, topH = 38f * (1.1f - squeeze * 0.08f);
                    ui.Art(fxLayer, topPath, targetBox.Cx - topW * 0.5f, targetBox.Ground - 76f, topW, topH, true, Mathf.Clamp01(enter * 1.1f));
                }
                return;
            }

            if (action == "heal_spray" || action == "antidote")
            {
                float enter = p < 0.3f ? p / 0.3f : p > 0.82f ? (1f - p) / 0.18f : 1f;
                ui.Art(fxLayer, GameUi.Item(action), actorBox.Cx + 34f + (1f - enter) * 90f, actorBox.Y + 18f, 44f, 44f, true, Mathf.Clamp01(enter));
                if (p >= 0.28f && p <= 0.82f)
                {
                    float pulse = Mathf.Abs(Mathf.Sin(((p - 0.28f) / 0.54f) * Mathf.PI * 2f));
                    ui.Art(fxLayer, FxPath("spray"), actorBox.Cx - 40f, actorBox.Y + 10f, 86f, 86f, true, pulse * 0.9f);
                }
                return;
            }

            if (action == "atk_pepper" || action == "def_berry" || action == "aim_shroom")
            {
                if (p < 0.72f)
                {
                    float t = p / 0.72f;
                    float ix = actorBox.Cx + 88f * (1f - t), iy = actorBox.Ground - 68f - Mathf.Sin(t * Mathf.PI) * 58f;
                    ui.Art(fxLayer, GameUi.Item(action), ix - 22f, iy - 22f, 44f, 44f, true, Mathf.Clamp01(1f - Mathf.Max(0f, (t - 0.75f) * 4f)));
                }
                else
                {
                    float glow = Mathf.Sin(((p - 0.72f) / 0.28f) * Mathf.PI) * 0.55f;
                    vec.FillRoundRect(actorBox.X + 8f, actorBox.Y + 16f, actorBox.W - 16f, actorBox.H - 20f, 20f, new Color(1f, 1f, 1f, glow));
                }
                return;
            }

            if (action.StartsWith("can_", StringComparison.Ordinal))
            {
                float canX = actorBox.Cx + 56f, canY = actorBox.Ground - 82f;
                if (p < 0.35f)
                    ui.Art(fxLayer, GameUi.Item(action), canX + (1f - p / 0.35f) * 70f, canY, 44f, 44f, true);
                else if (p < 0.78f)
                    DrawSheetFrame(ui, fxLayer, "can_open", 4, 1, (p - 0.35f) / 0.43f, canX - 10f, canY - 12f, 64f, 64f, 1f);
                else
                {
                    float orbP = (p - 0.78f) / 0.22f;
                    float ox = Mathf.Lerp(canX, actorBox.Cx, orbP), oy = Mathf.Lerp(canY + 20f, actorBox.Ground - 64f, orbP);
                    vec.FillCircle(ox, oy, 12f, new Color(1f, 1f, 1f, 0.9f));
                }
                return;
            }

            if (StepFxMap.TryGetValue(action, out string sheetName) && FxSheets.TryGetValue(sheetName, out var grid))
            {
                float fh = sheetName == "slash" ? 60f : 142f;
                DrawSheetFrame(ui, fxLayer, sheetName, grid.cols, grid.rows, p, targetBox.Cx - 68f, targetBox.Y + 8f, 136f, fh, 0.92f);
            }
        }

        public static void RenderRageBackFx(Transform layer, GameUi ui, CombatantView mon, BattleBox box, float timeMs)
        {
            if (mon == null || mon.Hp <= 0 || !mon.Rage) return;
            int idx = Mathf.FloorToInt(Mathf.Repeat(timeMs / 100f, 3f));
            ui.ArtCell(layer, FxPath("rage_fx_back"), 3, 1, idx, box.Cx - 64f, box.Ground - 130f, 128f, 128f, 0.85f, 0f, true);
        }

        public static void RenderStatusFx(Transform layer, VectorCanvas vec, GameUi ui, CombatantView mon, BattleBox box, float timeMs)
        {
            if (mon == null || mon.Hp <= 0) return;
            if (mon.Rage)
            {
                int[] seq = { 5, 6, 7 };
                int frame = seq[Mathf.FloorToInt(Mathf.Repeat(timeMs / 85f, seq.Length))];
                ui.ArtCell(layer, FxPath("rage_fx_front"), 4, 2, frame, box.Cx - 68f, box.Ground - 138f, 136f, 136f, 0.9f, 0f, true);
            }
            if (mon.Status == "sick")
            {
                float p = Mathf.Repeat(timeMs, 900f) / 900f;
                DrawSheetFrame(ui, layer, "sick_fx", 5, 1, p, box.Cx - 26f, box.Y - 6f, 52f, 36f, 0.9f);
            }
            else if (mon.Status == "hypno")
            {
                float deg = Mathf.Repeat(timeMs * 0.004f, Mathf.PI * 2f) * Mathf.Rad2Deg;
                ui.ArtCell(layer, FxPath("hypno_fx"), 1, 1, 0, box.Cx - 16f, box.Y - 6f, 32f, 32f, 0.9f, deg);
            }
            else if (mon.Status == "sleep")
            {
                for (int i = 0; i < 3; i++)
                {
                    float cycle = Mathf.Repeat(timeMs / 1400f + i * 0.33f, 1f);
                    float alpha = Mathf.Sin(cycle * Mathf.PI) * 0.95f;
                    float zx = box.Cx - 8f + i * 11f + Mathf.Sin(cycle * Mathf.PI * 2f) * 3f;
                    float zy = box.Y + 22f - cycle * 32f;
                    float s = 3.5f + i * 1.1f;
                    vec.StrokePoly(new[] { new Vector2(zx - s, zy - s), new Vector2(zx + s, zy - s), new Vector2(zx - s, zy + s), new Vector2(zx + s, zy + s) },
                        GameUi.HexColor(i % 2 == 0 ? "#f0d8ff" : "#fff0a8", alpha), 2.4f);
                }
            }
        }

        public static void RenderCatchFx(Transform fxLayer, GameUi ui, float progress, BattleBox targetBox, bool success)
        {
            float p = Mathf.Clamp01(progress);
            float glowAlpha = 0.45f + Mathf.Sin(p * Mathf.PI) * 0.55f;
            float cx = targetBox.Cx, cy = targetBox.Y + 74f;
            ui.Art(fxLayer, "session/ui/discatch_glow", cx - 32f, cy - 32f, 64f, 64f, true, glowAlpha);
            float wobble = p > 0.25f && p < 0.8f ? Mathf.Sin(p * 30f) * 4f : 0f;
            int idx = p < 0.72f ? (Mathf.FloorToInt(p * 12f) % 2) : success ? 2 : (3 + Mathf.Min(3, Mathf.FloorToInt(((p - 0.72f) / 0.28f) * 4f)));
            ui.ArtCell(fxLayer, "session/ui/catch_disk", 8, 1, idx, cx - 27f + wobble, cy - 28f, 54f, 56f, 1f, 0f, true);
        }

        public static void RenderImmuneFx(VectorCanvas vec, float progress, BattleBox targetBox, float timeMs = 0f, string variant = "void")
        {
            float p = Mathf.Clamp01(progress);
            float fade = p < 0.14f ? p / 0.14f : p > 0.78f ? (1f - p) / 0.22f : 1f;
            float cx = targetBox.Cx, cy = targetBox.Ground - 68f;
            bool isShield = variant == "shield";
            vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor(isShield ? "#032033" : "#0d0420", (isShield ? 0.25f : 0.28f) * fade));

            if (isShield)
            {
                vec.FillCircle(cx, cy, 58f, new Color(0.855f, 0.992f, 1f, 0.78f * fade), new Color(0.012f, 0.125f, 0.2f, 0f), 24);
                for (int i = 0; i < 2; i++)
                {
                    float rp = Mathf.Clamp01(p * 1.4f - i * 0.16f);
                    if (rp > 0f) vec.StrokeCircle(cx, cy, 18f + rp * 48f, i == 0 ? GameUi.HexColor("#3be5ff", fade * (1f - rp * 0.72f)) : new Color(1f, 1f, 1f, fade * (1f - rp * 0.72f)), 3.2f - rp * 1.4f);
                }
                if (p < 0.45f)
                {
                    float crackFade = 1f - p / 0.45f, hexRad = 35f + p * 12f;
                    var hex = Enumerable.Range(0, 6).Select(i => new Vector2(cx + Mathf.Cos(i * Mathf.PI / 3f) * hexRad, cy + Mathf.Sin(i * Mathf.PI / 3f) * hexRad)).ToArray();
                    vec.StrokePoly(hex, new Color(1f, 1f, 1f, crackFade), 2.8f, true);
                }
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * (Mathf.PI * 0.25f) + 0.25f, fly = 14f + p * 46f;
                    float sx = cx + Mathf.Cos(ang) * fly, sy = cy + Mathf.Sin(ang) * fly;
                    float spin = ang + p * (i % 2 == 0 ? 3.4f : -3.4f), sz = 7.5f * (1f - p * 0.3f);
                    var o = new Vector2(sx, sy);
                    var poly = new[] { Rot(o, 0f, -sz, spin), Rot(o, sz * 0.75f, 0f, spin), Rot(o, 0f, sz, spin), Rot(o, -sz * 0.65f, 0f, spin) };
                    vec.FillPoly(poly, i % 2 == 0 ? GameUi.HexColor("#7ff5ff", fade) : new Color(1f, 1f, 1f, fade));
                    vec.StrokePoly(poly, GameUi.HexColor("#064f87", fade), 1.6f, true);
                }
                return;
            }

            vec.FillCircle(cx, cy, 56f, new Color(0.941f, 0.831f, 1f, 0.75f * fade), new Color(0.078f, 0.024f, 0.188f, 0f), 24);
            for (int i = 0; i < 2; i++)
            {
                float rp = Mathf.Clamp01(p * 1.35f - i * 0.18f);
                if (rp > 0f) vec.StrokeCircle(cx, cy, 18f + rp * 50f, GameUi.HexColor(i == 0 ? "#c06eff" : "#6ef3ff", fade * (1f - rp * 0.7f)), 3f - rp * 1.4f);
            }
            float voidRad = 32f + Mathf.Sin(p * Mathf.PI) * 14f, voidRot = p * 1.2f + timeMs * 0.001f;
            var voidHex = Enumerable.Range(0, 6).Select(i => new Vector2(cx + Mathf.Cos(i * Mathf.PI / 3f + voidRot) * voidRad, cy + Mathf.Sin(i * Mathf.PI / 3f + voidRot) * voidRad)).ToArray();
            vec.StrokePoly(voidHex, GameUi.HexColor("#6e1ec9", fade * 0.65f), 7f, true);
            vec.StrokePoly(voidHex, GameUi.HexColor("#eabfff", fade * 0.96f), 2.6f, true);
            for (int i = 0; i < 6; i++)
            {
                float ang = i * (Mathf.PI / 3f) + 0.4f + p * 0.5f, inner = 26f + p * 14f, outer = 40f + p * 34f;
                vec.StrokePoly(new[] { new Vector2(cx + Mathf.Cos(ang) * inner, cy + Mathf.Sin(ang) * inner), new Vector2(cx + Mathf.Cos(ang) * outer, cy + Mathf.Sin(ang) * outer) },
                    GameUi.HexColor(i % 2 == 0 ? "#72f2ff" : "#d599ff", fade * 0.85f), 2.6f);
            }
        }

        public static void RenderLevelupBlastFrame(Transform layer, GameUi ui, string phase, float progress, float cx, float bottomY, float scale = 1f, float alpha = 1f)
        {
            Vector2 tex = ui.ArtSize("general/levelup_blast");
            if (tex == Vector2.zero) return;
            var seq = LevelupSequences.TryGetValue(phase ?? "blast", out var s) ? s : LevelupSequences["blast"];
            int idx = seq[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp(progress, 0f, 0.999f) * seq.Length), 0, seq.Length - 1)];
            float sw = tex.x / 7f, sh = tex.y / 2f;
            float dw = (sw * 0.5f) * 1.45f * scale, dh = (sh * 0.5f) * 1.45f * scale;
            ui.ArtCell(layer, "general/levelup_blast", 7, 2, idx, cx - dw * 0.5f, bottomY - dh + 20f * scale, dw, dh, alpha);
        }

        public static void RenderMegaAura(VectorCanvas vec, BattleBox box, float timeMs = 0f)
        {
            float t = timeMs * 0.004f;
            float pulse = 0.85f + Mathf.Sin(t * 1.6f) * 0.15f;
            float cx = box.Cx, cy = box.Ground - box.H * 0.48f;
            float r = Mathf.Max(box.W, box.H) * 0.62f * pulse;
            float h = Mathf.Repeat(timeMs * 0.00038f, 1f);
            Color c0 = Color.HSVToRGB(h, 0.85f, 1f); c0.a = 0.42f;
            Color c1 = Color.HSVToRGB(Mathf.Repeat(h + 0.25f, 1f), 0.95f, 0.9f); c1.a = 0f;
            vec.FillCircle(cx, cy, r, c0, c1, 24);
            for (int i = 0; i < 7; i++)
            {
                float life = Mathf.Repeat(timeMs * 0.00095f + i / 7f, 1f);
                float sx = cx + Mathf.Sin(i * 2.3f + t) * (box.W * 0.42f);
                float sy = box.Ground - 10f - life * (box.H * 0.92f);
                Color sc = RainbowColors[i % RainbowColors.Length];
                sc.a = Mathf.Sin(life * Mathf.PI) * 0.9f;
                vec.StarFlare(sx, sy, 3.5f + (1f - life) * 3.5f, sc);
            }
        }

        public static void RenderMegaEyes(VectorCanvas vec, Rect spriteRect, string mode = "back", float timeMs = 0f, string monId = null)
        {
            float t = timeMs * 0.006f;
            float pulse = 0.86f + Mathf.Sin(t * 2.1f) * 0.22f;
            EyeAnchor[] anchors = null;
            if (!string.IsNullOrEmpty(monId) && MegaEyeAnchors.TryGetValue(monId, out var pair))
                anchors = mode == "front" ? pair.front : pair.back;
            anchors ??= mode == "front"
                ? new[] { new EyeAnchor(0.37f, 0.29f, 1.1f), new EyeAnchor(0.61f, 0.29f, 1.1f) }
                : new[] { new EyeAnchor(0.60f, 0.28f, 1.0f) };

            float hue = Mathf.Repeat((timeMs * 0.16f + 185f) / 360f, 1f);
            Color glowCenter = Color.HSVToRGB(hue, 0.7f, 1f); glowCenter.a = 0.85f;
            Color glowRim = Color.HSVToRGB(Mathf.Repeat(hue + 0.14f, 1f), 0.9f, 0.9f); glowRim.a = 0f;

            foreach (var anchor in anchors)
            {
                float ex = spriteRect.x + spriteRect.width * anchor.Rx;
                float ey = spriteRect.y + spriteRect.height * anchor.Ry;
                float rad = 18f * anchor.Scale * pulse;
                vec.FillCircle(ex, ey, rad, glowCenter, glowRim, 18);

                float streakLen = (mode == "front" ? 38f : 34f) * anchor.Scale * pulse;
                float tilt = mode == "front" ? -0.14f : -0.24f;
                var o = new Vector2(ex, ey);
                vec.FillPoly(new[]
                {
                    Rot(o, -streakLen * 0.65f, 0f, tilt), Rot(o, 0f, -3.2f * anchor.Scale, tilt),
                    Rot(o, streakLen, 0f, tilt), Rot(o, 0f, 3.2f * anchor.Scale, tilt),
                }, new Color(0.75f, 0.97f, 1f, 0.92f));
                vec.FillPoly(new[]
                {
                    Rot(o, 0f, -14f * anchor.Scale * pulse, tilt), Rot(o, 2.4f * anchor.Scale, 0f, tilt),
                    Rot(o, 0f, 14f * anchor.Scale * pulse, tilt), Rot(o, -2.4f * anchor.Scale, 0f, tilt),
                }, new Color(1f, 1f, 1f, 0.92f));
                vec.FillCircle(ex, ey, 3.6f * anchor.Scale, Color.white);
            }
        }

        public static void RenderMegaTransform(Transform fxLayer, VectorCanvas vec, GameUi ui, float progress, string monId, float timeMs = 0f)
        {
            float p = Mathf.Clamp01(progress);
            float fade = p < 0.15f ? p / 0.15f : p > 0.85f ? (1f - p) / 0.15f : 1f;
            vec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, GameUi.HexColor("#08041c", 0.72f * fade));
            ui.Art(fxLayer, "shop/loot_lights", 0f, 0f, GameUi.Width, GameUi.Height, true, 0.78f * fade);

            for (int i = 0; i < 4; i++)
            {
                float rp = Mathf.Clamp01(p * 1.4f - i * 0.14f);
                if (rp <= 0f || rp >= 1f) continue;
                Color rc = RainbowColors[(i * 2) % RainbowColors.Length];
                rc.a = (1f - rp) * 0.85f;
                vec.StrokeCircle(GameUi.Width * 0.5f, 260f, 26f + rp * 165f, rc, 4f - rp * 2.5f);
            }

            string phase = p < 0.28f ? "start" : p < 0.78f ? "blast" : "fade";
            float phaseP = p < 0.28f ? p / 0.28f : p < 0.78f ? (p - 0.28f) / 0.5f : (p - 0.78f) / 0.22f;
            RenderLevelupBlastFrame(fxLayer, ui, phase, phaseP, GameUi.Width * 0.5f, 336f, 1.15f, 0.9f * fade);

            string spritePath = GameUi.Mon(monId, "front");
            ui.Art(fxLayer, spritePath, GameUi.Width * 0.5f - 74f, 174f, 148f, 152f, true, fade);
            Vector2 tex = ui.ArtSize(spritePath);
            Rect eyeRect = new Rect(GameUi.Width * 0.5f - 74f, 174f, 148f, 152f);
            if (tex.x > 0f && tex.y > 0f)
            {
                float s = Mathf.Min(148f / tex.x, 152f / tex.y);
                float dw = tex.x * s, dh = tex.y * s;
                eyeRect = new Rect(eyeRect.x + (148f - dw) * 0.5f, eyeRect.y + (152f - dh) * 0.5f, dw, dh);
            }
            var eyeVec = VectorCanvas.Create("MegaTransformEyes", fxLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            RenderMegaEyes(eyeVec, eyeRect, "front", timeMs + p * 600f, monId);
            if (p > 0.35f && p < 0.62f)
            {
                float flash = Mathf.Sin(((p - 0.35f) / 0.27f) * Mathf.PI) * 0.68f;
                eyeVec.FillRect(0f, 0f, GameUi.Width, GameUi.Height, new Color(1f, 1f, 1f, flash));
            }
            eyeVec.Commit();
        }
    }
}
