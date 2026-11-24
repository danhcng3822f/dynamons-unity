using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Dynamons
{
    internal sealed class BitmapFontRegistry
    {
        public readonly struct TextMetrics
        {
            public readonly float Width, Height, Top, Bottom, Baseline;
            public TextMetrics(float width, float height, float top, float bottom, float baseline)
            {
                Width = width;
                Height = height;
                Top = top;
                Bottom = bottom;
                Baseline = baseline;
            }
        }

        private sealed class FontEntry
        {
            public string Id;
            public FontData Data;
            public float NaturalHeight;
            public readonly Dictionary<int, GlyphMetric> Glyphs = new Dictionary<int, GlyphMetric>();
            public readonly Dictionary<(int, int), float> Kerns = new Dictionary<(int, int), float>();
        }

        private sealed class ResolvedGlyph
        {
            public FontEntry Font;
            public int Code;
            public GlyphMetric Metric;
        }

        private struct LayoutRun
        {
            public string FontId;
            public int Code;
            public float X, Y, W, H;
        }

        private static readonly Dictionary<string, string[]> Fallbacks = new Dictionary<string, string[]>
        {
            { "black_small", Array.Empty<string>() },
            { "white_small", Array.Empty<string>() },
            { "black_medium", new[] { "black_small" } },
            { "white_shadow_small", new[] { "white_small" } },
            { "white_stroked_small", new[] { "white_small" } },
            { "white_stroked_big", new[] { "white_stroked_small", "white_small" } },
            { "red_stroked_small", new[] { "white_stroked_small", "white_small" } },
        };

        private static readonly Color FallbackRedColor = new Color32(255, 105, 92, 255);
        private static readonly Regex HardSplitRegex = new Regex(@"\||\r?\n", RegexOptions.Compiled);
        private readonly Dictionary<string, FontEntry> fonts = new Dictionary<string, FontEntry>();
        private readonly GameUi ui;

        public BitmapFontRegistry(GameData data, GameUi ui)
        {
            this.ui = ui;
            if (data?.fonts == null) return;
            foreach (var font in data.fonts)
            {
                if (font == null || string.IsNullOrEmpty(font.id)) continue;
                var entry = new FontEntry
                {
                    Id = font.id,
                    Data = font,
                    NaturalHeight = Mathf.Max(1f, font.baseline - font.capTop),
                };
                if (font.glyphs != null)
                    foreach (var g in font.glyphs) entry.Glyphs[g.code] = g;
                if (font.kerns != null)
                    foreach (var k in font.kerns) entry.Kerns[(k.left, k.right)] = k.adjust;
                fonts[font.id] = entry;
            }
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch == '\u2018' || ch == '\u2019' || ch == '\u201a' || ch == '\u201b' ||
                    ch == '\u201c' || ch == '\u201d' || ch == '\u201e' || ch == '\u201f' || ch == '"')
                    sb.Append('\'');
                else if (ch >= '\u2010' && ch <= '\u2015' || ch == '\u2212')
                    sb.Append('-');
                else if (ch == '\u2026')
                    sb.Append("...");
                else if (char.IsWhiteSpace(ch))
                    sb.Append(' ');
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        private List<FontEntry> CandidateFonts(string fontId)
        {
            var list = new List<FontEntry>(3);
            if (!string.IsNullOrEmpty(fontId) && fonts.TryGetValue(fontId, out var primary))
                list.Add(primary);
            if (!string.IsNullOrEmpty(fontId) && Fallbacks.TryGetValue(fontId, out var chain))
            {
                foreach (var fid in chain)
                    if (fonts.TryGetValue(fid, out var fb)) list.Add(fb);
            }
            return list;
        }

        private ResolvedGlyph Resolve(List<FontEntry> candidates, int code)
        {
            string upper = char.ConvertFromUtf32(code).ToUpperInvariant();
            int upperCode = upper.Length == 1 && upper[0] != code ? upper[0] : -1;

            for (int step = 0; step < 3; step++)
            {
                int cand = step == 0 ? code : step == 1 ? upperCode : 63;
                if (cand < 0) continue;
                for (int f = 0; f < candidates.Count; f++)
                {
                    var font = candidates[f];
                    if (font.Glyphs.TryGetValue(cand, out var metric))
                        return new ResolvedGlyph { Font = font, Code = cand, Metric = metric };
                }
            }
            return null;
        }

        private static float KernBetween(FontEntry font, int prevCode, int code)
        {
            if (prevCode == 32 || code == 32) return 0f;
            if (font.Kerns.TryGetValue((prevCode, code), out var exact)) return exact;
            if (font.Kerns.TryGetValue((prevCode, 0), out var rowAny)) return rowAny;
            if (font.Kerns.TryGetValue((0, 0), out var fallbackAny)) return fallbackAny;
            return 0f;
        }

        private TextMetrics Layout(string fontId, string text, float size, List<LayoutRun> runs)
        {
            runs?.Clear();
            var candidates = CandidateFonts(fontId);
            if (candidates.Count == 0 || size <= 0f)
                return new TextMetrics(0f, 0f, 0f, 0f, size);

            float width = 0f;
            float top = float.PositiveInfinity;
            float bottom = float.NegativeInfinity;
            ResolvedGlyph previous = null;

            string norm = Normalize(text);
            foreach (char ch in norm)
            {
                int code = ch;
                var glyph = Resolve(candidates, code);
                if (glyph == null) continue;
                float scale = size / glyph.Font.NaturalHeight;
                if (previous != null && ReferenceEquals(previous.Font, glyph.Font))
                    width += KernBetween(glyph.Font, previous.Code, glyph.Code) * scale;

                var g = glyph.Metric;
                float y = size - glyph.Font.Data.baseline * scale;
                if (g.top >= 0)
                {
                    top = Mathf.Min(top, y + g.top * scale);
                    bottom = Mathf.Max(bottom, y + g.bottom * scale);
                    runs?.Add(new LayoutRun
                    {
                        FontId = glyph.Font.Id,
                        Code = glyph.Code,
                        X = width,
                        Y = y,
                        W = g.w * scale,
                        H = g.h * scale,
                    });
                }
                width += g.w * scale;
                previous = ch == ' ' ? null : glyph;
            }

            if (float.IsPositiveInfinity(top)) { top = 0f; bottom = 0f; }
            return new TextMetrics(width, bottom - top, top, bottom, size);
        }

        public float Measure(string fontId, string text, float size) => Layout(fontId, text, size, null).Width;

        public TextMetrics Metrics(string fontId, string text, float size) => Layout(fontId, text, size, null);

        public float Draw(Transform parent, string fontId, string text, float x, float y, float size,
            string align = "left", Color? color = null, float maxWidth = 0f)
        {
            if (parent == null || string.IsNullOrEmpty(text)) return x;
            if (maxWidth > 0f)
            {
                float measured = Measure(fontId, text, size);
                if (measured > maxWidth && measured > 0.001f)
                    size = Mathf.Max(5.5f, Mathf.Min(size, size * (maxWidth / measured)));
            }

            var runs = new List<LayoutRun>(text.Length);
            var metrics = Layout(fontId, text, size, runs);
            float start = align == "center" ? x - metrics.Width * 0.5f : align == "right" ? x - metrics.Width : x;

            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var img = ui.Art(parent, $"fonts/{run.FontId}/c{run.Code}", start + run.X, y + run.Y, run.W, run.H);
                if (img == null) continue;
                if (color.HasValue) img.color = color.Value;
                else if (fontId == "red_stroked_small" && run.FontId != "red_stroked_small") img.color = FallbackRedColor;
            }
            return start + metrics.Width;
        }

        public float DrawLines(Transform parent, string fontId, string text, float x, float y, float size,
            float maxWidth = 0f, float lineHeight = 0f, string align = "left", Color? color = null)
        {
            if (lineHeight <= 0f) lineHeight = size * 1.25f;
            var lines = Wrap(fontId, text ?? string.Empty, size, maxWidth);
            for (int i = 0; i < lines.Count; i++)
                Draw(parent, fontId, lines[i], x, y + i * lineHeight, size, align, color);
            return lines.Count * lineHeight;
        }

        private List<string> Wrap(string fontId, string text, float size, float maxWidth)
        {
            var hard = HardSplitRegex.Split(text ?? string.Empty);
            if (maxWidth <= 0f) return new List<string>(hard);
            var outLines = new List<string>(hard.Length);
            foreach (var paragraph in hard)
            {
                var words = paragraph.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string line = string.Empty;
                foreach (var word in words)
                {
                    string candidate = line.Length > 0 ? line + " " + word : word;
                    if (Measure(fontId, candidate, size) > maxWidth && line.Length > 0)
                    {
                        outLines.Add(line);
                        line = word;
                    }
                    else line = candidate;
                }
                outLines.Add(line);
            }
            return outLines;
        }
    }
}
