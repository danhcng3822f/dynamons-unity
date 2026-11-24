using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dynamons
{
    internal sealed class VectorCanvas : MaskableGraphic
    {
        private const int MaxVerts = 60000;
        private readonly List<UIVertex> verts = new List<UIVertex>(512);
        private readonly List<int> indices = new List<int>(768);

        private static readonly Color[] RainbowColors =
        {
            new Color32(0xff, 0x2e, 0x4c, 0xff),
            new Color32(0xff, 0x8c, 0x00, 0xff),
            new Color32(0xff, 0xe6, 0x00, 0xff),
            new Color32(0x22, 0xe5, 0x5b, 0xff),
            new Color32(0x00, 0xd9, 0xff, 0xff),
            new Color32(0x3b, 0x6d, 0xff, 0xff),
            new Color32(0xb4, 0x3b, 0xff, 0xff),
        };

        public static VectorCanvas Create(string name, Transform parent, float x = 0f, float y = 0f, float w = GameUi.Width, float h = GameUi.Height)
        {
            var box = GameUi.Box(name, parent, x, y, w, h);
            var canvas = box.gameObject.AddComponent<VectorCanvas>();
            canvas.raycastTarget = false;
            return canvas;
        }

        public void Clear()
        {
            verts.Clear();
            indices.Clear();
        }

        public void Commit() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (verts.Count == 0 || indices.Count == 0) return;
            vh.AddUIVertexStream(verts, indices);
        }

        private static Color ClampColor(Color c) => new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a));

        private void AddTri(Vector2 p0, Color c0, Vector2 p1, Color c1, Vector2 p2, Color c2)
        {
            if (verts.Count + 3 > MaxVerts) return;
            int baseIdx = verts.Count;
            var uv = Vector4.zero;
            verts.Add(new UIVertex { position = new Vector3(p0.x, -p0.y, 0f), color = ClampColor(c0), uv0 = uv });
            verts.Add(new UIVertex { position = new Vector3(p1.x, -p1.y, 0f), color = ClampColor(c1), uv0 = uv });
            verts.Add(new UIVertex { position = new Vector3(p2.x, -p2.y, 0f), color = ClampColor(c2), uv0 = uv });
            indices.Add(baseIdx);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);
        }

        private void AddQuad(Vector2 p0, Color c0, Vector2 p1, Color c1, Vector2 p2, Color c2, Vector2 p3, Color c3)
        {
            if (verts.Count + 4 > MaxVerts) return;
            int baseIdx = verts.Count;
            var uv = Vector4.zero;
            verts.Add(new UIVertex { position = new Vector3(p0.x, -p0.y, 0f), color = ClampColor(c0), uv0 = uv });
            verts.Add(new UIVertex { position = new Vector3(p1.x, -p1.y, 0f), color = ClampColor(c1), uv0 = uv });
            verts.Add(new UIVertex { position = new Vector3(p2.x, -p2.y, 0f), color = ClampColor(c2), uv0 = uv });
            verts.Add(new UIVertex { position = new Vector3(p3.x, -p3.y, 0f), color = ClampColor(c3), uv0 = uv });
            indices.Add(baseIdx);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx + 3);
        }

        public void FillRect(float x, float y, float w, float h, Color color)
        {
            if (w <= 1e-4f || h <= 1e-4f || color.a <= 0f) return;
            AddQuad(new Vector2(x, y), color, new Vector2(x + w, y), color,
                new Vector2(x + w, y + h), color, new Vector2(x, y + h), color);
        }

        private static List<Vector2> SampleRoundRectPerimeter(float x, float y, float w, float h, float radius, int arcSteps = 6)
        {
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            var pts = new List<Vector2>(4 * (arcSteps + 1));
            if (r < 0.25f)
            {
                pts.Add(new Vector2(x, y));
                pts.Add(new Vector2(x + w, y));
                pts.Add(new Vector2(x + w, y + h));
                pts.Add(new Vector2(x, y + h));
                return pts;
            }
            AddCorner(pts, x + w - r, y + r, r, -Mathf.PI * 0.5f, 0f, arcSteps);
            AddCorner(pts, x + w - r, y + h - r, r, 0f, Mathf.PI * 0.5f, arcSteps);
            AddCorner(pts, x + r, y + h - r, r, Mathf.PI * 0.5f, Mathf.PI, arcSteps);
            AddCorner(pts, x + r, y + r, r, Mathf.PI, Mathf.PI * 1.5f, arcSteps);
            return pts;
        }

        private static void AddCorner(List<Vector2> pts, float cx, float cy, float r, float a0, float a1, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps);
                pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r));
            }
        }

        public void FillRoundRect(float x, float y, float w, float h, float radius, Color color)
        {
            if (w <= 1e-4f || h <= 1e-4f || color.a <= 0f) return;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            if (r < 0.25f) { FillRect(x, y, w, h, color); return; }
            var pts = SampleRoundRectPerimeter(x, y, w, h, r, 6);
            var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
            for (int i = 0; i < pts.Count; i++)
                AddTri(center, color, pts[i], color, pts[(i + 1) % pts.Count], color);
        }

        public void StrokeRect(float x, float y, float w, float h, Color color, float thickness = 2f)
        {
            if (thickness <= 1e-4f || color.a <= 0f) return;
            float half = thickness * 0.5f;
            FillRect(x - half, y - half, w + thickness, thickness, color);
            FillRect(x - half, y + h - half, w + thickness, thickness, color);
            if (h > thickness)
            {
                FillRect(x - half, y + half, thickness, h - thickness, color);
                FillRect(x + w - half, y + half, thickness, h - thickness, color);
            }
        }

        public void StrokeRoundRect(float x, float y, float w, float h, float radius, Color color, float thickness = 2f)
        {
            if (w <= 1e-4f || h <= 1e-4f || thickness <= 1e-4f || color.a <= 0f) return;
            var pts = SampleRoundRectPerimeter(x, y, w, h, radius, 6);
            StrokePoly(pts, color, thickness, true);
        }

        public void StrokeRoundRectGradient(float x, float y, float w, float h, float radius, Color[] colors, float phaseRad, float thickness = 3.5f)
        {
            if (w <= 1e-4f || h <= 1e-4f || thickness <= 1e-4f || colors == null || colors.Length == 0) return;
            var pts = SampleRoundRectPerimeter(x, y, w, h, radius, 8);
            if (pts.Count < 2) return;

            float cx = x + w * 0.5f, cy = y + h * 0.5f;
            float dx = Mathf.Cos(phaseRad) * (w * 0.72f);
            float dy = Mathf.Sin(phaseRad) * (h * 0.72f);
            var a = new Vector2(cx - dx, cy - dy);
            var ab = new Vector2(dx * 2f, dy * 2f);
            float denom = Mathf.Max(1e-4f, ab.sqrMagnitude);

            Color SampleColor(Vector2 p)
            {
                float t = Vector2.Dot(p - a, ab) / denom;
                t = Mathf.Repeat(t, 1f);
                float scaled = t * colors.Length;
                int i0 = Mathf.FloorToInt(scaled) % colors.Length;
                int i1 = (i0 + 1) % colors.Length;
                return Color.Lerp(colors[i0], colors[i1], scaled - Mathf.Floor(scaled));
            }

            float half = thickness * 0.5f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                var d = p1 - p0;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                var n = new Vector2(-d.y, d.x) / len * half;
                Color c0 = SampleColor(p0), c1 = SampleColor(p1);
                AddQuad(p0 - n, c0, p0 + n, c0, p1 + n, c1, p1 - n, c1);
            }
        }

        public void FillCircle(float cx, float cy, float radius, Color centerColor, Color? rimColor = null, int segments = 24)
        {
            if (radius <= 1e-4f) return;
            segments = Mathf.Max(6, segments);
            var center = new Vector2(cx, cy);
            var rim = rimColor ?? centerColor;
            if (centerColor.a <= 0f && rim.a <= 0f) return;
            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                AddTri(center, centerColor,
                    new Vector2(cx + Mathf.Cos(a0) * radius, cy + Mathf.Sin(a0) * radius), rim,
                    new Vector2(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius), rim);
            }
        }

        public void StrokeCircle(float cx, float cy, float radius, Color color, float thickness = 2f,
            float startRad = 0f, float endRad = Mathf.PI * 2f, int segments = 24, float? radiusY = null)
        {
            float ry = radiusY ?? radius;
            if (radius <= 1e-4f || ry <= 1e-4f || thickness <= 1e-4f || color.a <= 0f) return;
            segments = Mathf.Max(4, segments);
            bool full = Mathf.Abs(Mathf.Abs(endRad - startRad) - Mathf.PI * 2f) < 0.01f;
            var pts = new List<Vector2>(segments + 1);
            int count = full ? segments : segments + 1;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Lerp(startRad, endRad, i / (float)segments);
                pts.Add(new Vector2(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * ry));
            }
            StrokePoly(pts, color, thickness, full);
        }

        public void StrokePoly(IList<Vector2> points, Color color, float thickness = 2f, bool closed = false)
        {
            if (points == null || points.Count < 2 || thickness <= 1e-4f || color.a <= 0f) return;
            float half = thickness * 0.5f;
            int segCount = closed ? points.Count : points.Count - 1;
            Vector2 prevN = Vector2.zero;
            bool hasPrev = false;

            for (int i = 0; i < segCount; i++)
            {
                var p0 = points[i];
                var p1 = points[(i + 1) % points.Count];
                var d = p1 - p0;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                var n = new Vector2(-d.y, d.x) / len * half;
                AddQuad(p0 - n, color, p0 + n, color, p1 + n, color, p1 - n, color);
                if (hasPrev)
                {
                    AddTri(p0, color, p0 + prevN, color, p0 + n, color);
                    AddTri(p0, color, p0 - prevN, color, p0 - n, color);
                }
                prevN = n;
                hasPrev = true;
            }
            if (closed && hasPrev)
            {
                var d0 = points[1] - points[0];
                float len0 = d0.magnitude;
                if (len0 >= 1e-4f)
                {
                    var n0 = new Vector2(-d0.y, d0.x) / len0 * half;
                    AddTri(points[0], color, points[0] + prevN, color, points[0] + n0, color);
                    AddTri(points[0], color, points[0] - prevN, color, points[0] - n0, color);
                }
            }
        }

        public void FillPoly(IList<Vector2> points, Color color)
        {
            if (points == null || points.Count < 3 || color.a <= 0f) return;
            int n = points.Count;
            if (n == 3) { AddTri(points[0], color, points[1], color, points[2], color); return; }

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            bool ccw = area >= 0f;
            if (!ccw) idx.Reverse();

            int guard = n * n;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = points[i0], b = points[i1], c = points[i2];
                    if (Cross(b - a, c - a) <= 1e-4f) continue;

                    bool anyInside = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        int k = idx[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTri(points[k], a, b, c)) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    AddTri(a, color, b, color, c, color);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }

            if (idx.Count == 3)
            {
                AddTri(points[idx[0]], color, points[idx[1]], color, points[idx[2]], color);
            }
            else if (idx.Count > 3)
            {
                Vector2 center = Vector2.zero;
                foreach (int k in idx) center += points[k];
                center /= idx.Count;
                for (int i = 0; i < idx.Count; i++)
                    AddTri(center, color, points[idx[i]], color, points[idx[(i + 1) % idx.Count]], color);
            }
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float c1 = Cross(b - a, p - a);
            float c2 = Cross(c - b, p - b);
            float c3 = Cross(a - c, p - c);
            return c1 >= -1e-4f && c2 >= -1e-4f && c3 >= -1e-4f;
        }

        public void StarFlare(float cx, float cy, float radius, Color color)
        {
            if (radius <= 1e-4f || color.a <= 0f) return;
            var center = new Vector2(cx, cy);
            var ring = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float r = (i % 2 == 0) ? radius : radius * 0.22f;
                float a = i * (Mathf.PI * 0.25f);
                ring[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            for (int i = 0; i < 8; i++)
                AddTri(center, color, ring[i], color, ring[(i + 1) % 8], color);
        }

        public void LightningBolt(float x1, float y1, float x2, float y2, float seed, Color color, int segments = 7, float jagged = 10f)
        {
            segments = Mathf.Max(2, segments);
            float dx = x2 - x1, dy = y2 - y1;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f || color.a <= 0f) return;
            var normal = new Vector2(-dy / len, dx / len);

            var pts = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float off = (i > 0 && i < segments) ? Mathf.Sin(seed * 12.7f + i * 7.3f) * jagged : 0f;
                pts.Add(new Vector2(x1 + dx * t + normal.x * off, y1 + dy * t + normal.y * off));
            }

            StrokePoly(pts, new Color(color.r, color.g, color.b, color.a * 0.28f), 6f);
            StrokePoly(pts, new Color(color.r, color.g, color.b, color.a * 0.7f), 3f);
            StrokePoly(pts, new Color(1f, 1f, 1f, color.a * 0.95f), 1.3f);
        }

        public void DrawRainbowBorder(float x, float y, float w, float h, float timeMs = 0f,
            float thickness = 3.5f, float radius = 8f, bool glow = true)
        {
            float t = timeMs * 0.0028f;
            if (glow)
            {
                float glowAlpha = 0.5f + Mathf.Sin(t * 2.2f) * 0.2f;
                var glowColors = new Color[RainbowColors.Length];
                for (int i = 0; i < RainbowColors.Length; i++)
                    glowColors[i] = new Color(RainbowColors[i].r, RainbowColors[i].g, RainbowColors[i].b, glowAlpha);
                StrokeRoundRectGradient(x - 1f, y - 1f, w + 2f, h + 2f, radius + 1f, glowColors, t, thickness + 3.5f);
            }

            StrokeRoundRectGradient(x, y, w, h, radius, RainbowColors, t, thickness);

            float cx = x + w * 0.5f, cy = y + h * 0.5f;
            float perimT = Mathf.Repeat(timeMs * 0.0011f, 1f);
            float angle = perimT * Mathf.PI * 2f;
            float gx = Mathf.Clamp(cx + Mathf.Cos(angle) * (w * 0.52f), x + 2f, x + w - 2f);
            float gy = Mathf.Clamp(cy + Mathf.Sin(angle) * (h * 0.52f), y + 2f, y + h - 2f);
            StarFlare(gx, gy, 6.5f, new Color(1f, 1f, 1f, 0.95f));
        }
    }
}
