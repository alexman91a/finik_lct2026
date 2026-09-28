using System;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Tiny signed-distance rasterizer used to bake crisp, anti-aliased UI sprites in the editor.
    /// Coordinates are pixels, origin bottom-left, y up. Negative distance = inside.
    /// </summary>
    public sealed class FinikSdfCanvas
    {
        public delegate float Sdf(Vector2 p);
        public delegate Color Paint(Vector2 p);

        public readonly int Width;
        public readonly int Height;
        readonly Color[] pixels;

        public FinikSdfCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            pixels = new Color[width * height];
        }

        public Vector2 Center => new(Width * 0.5f, Height * 0.5f);

        /// <summary>Composites a shape over the canvas with ~1px anti-aliasing.</summary>
        public void Fill(Sdf shape, Paint paint, float feather = 1f)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = shape(p);
                float coverage = Mathf.Clamp01(0.5f - d / feather);
                if (coverage <= 0f) continue;
                Color c = paint(p);
                c.a *= coverage;
                Over(y * Width + x, c);
            }
        }

        public void Fill(Sdf shape, Color color, float feather = 1f) => Fill(shape, _ => color, feather);

        /// <summary>Soft shadow / glow: alpha falls off smoothly over <paramref name="blur"/> px outside the shape.</summary>
        public void Glow(Sdf shape, Color color, float blur)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float d = shape(new Vector2(x + 0.5f, y + 0.5f));
                float t = Mathf.Clamp01(1f - d / blur);
                float a = t * t * (3f - 2f * t);
                if (a <= 0f) continue;
                Color c = color;
                c.a *= a;
                Over(y * Width + x, c);
            }
        }

        /// <summary>Stamps an existing texture (e.g. a portrait) into a region, clipped by a shape.</summary>
        public void Stamp(Texture2D source, Rect destination, Sdf clip)
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!destination.Contains(p)) continue;
                float coverage = Mathf.Clamp01(0.5f - clip(p));
                if (coverage <= 0f) continue;
                float u = (p.x - destination.x) / destination.width;
                float v = (p.y - destination.y) / destination.height;
                Color c = source.GetPixelBilinear(u, v);
                c.a *= coverage;
                Over(y * Width + x, c);
            }
        }

        void Over(int index, Color src)
        {
            Color dst = pixels[index];
            float outA = src.a + dst.a * (1f - src.a);
            if (outA <= 0f)
            {
                pixels[index] = default;
                return;
            }
            float k = dst.a * (1f - src.a);
            pixels[index] = new Color(
                (src.r * src.a + dst.r * k) / outA,
                (src.g * src.a + dst.g * k) / outA,
                (src.b * src.a + dst.b * k) / outA,
                outA);
        }

        public Texture2D ToTexture()
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            // Bleed colour into fully transparent pixels so bilinear filtering never pulls in black fringes.
            var output = (Color[])pixels.Clone();
            for (int i = 0; i < output.Length; i++)
                if (output[i].a <= 0f) output[i] = WithAlpha(NearestColor(i), 0f);
            texture.SetPixels(output);
            texture.Apply(false);
            return texture;
        }

        Color NearestColor(int index)
        {
            int x = index % Width, y = index / Width;
            for (int r = 1; r <= 3; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                Color c = pixels[ny * Width + nx];
                if (c.a > 0f) return c;
            }
            return Color.white;
        }

        // ---------- Shapes ----------

        public static Sdf Circle(Vector2 c, float r) => p => (p - c).magnitude - r;

        public static Sdf Ellipse(Vector2 c, Vector2 radii)
        {
            float m = Mathf.Min(radii.x, radii.y);
            return p =>
            {
                Vector2 q = new((p.x - c.x) / radii.x, (p.y - c.y) / radii.y);
                return (q.magnitude - 1f) * m;
            };
        }

        public static Sdf RoundBox(Rect rect, float radius)
        {
            Vector2 c = rect.center;
            Vector2 half = rect.size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            return p =>
            {
                Vector2 q = new(Mathf.Abs(p.x - c.x) - half.x + radius, Mathf.Abs(p.y - c.y) - half.y + radius);
                return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            };
        }

        public static Sdf Capsule(Vector2 a, Vector2 b, float r) => p =>
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        };

        /// <summary>Exact polygon distance (Inigo Quilez). Vertices in any winding.</summary>
        public static Sdf Polygon(params Vector2[] v) => p =>
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s *= -1f;
            }
            return s * Mathf.Sqrt(d);
        };

        public static Sdf Star(Vector2 c, float outer, float inner, int points, float rotationDeg = 90f)
        {
            var verts = new Vector2[points * 2];
            for (int i = 0; i < verts.Length; i++)
            {
                float a = (rotationDeg + i * 180f / points) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? outer : inner;
                verts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return Polygon(verts);
        }

        public static Sdf Gear(Vector2 c, float radius, int teeth, float toothDepth, float toothWidthDeg, float hole)
        {
            return p =>
            {
                Vector2 d = p - c;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float step = 360f / teeth;
                float local = Mathf.Abs(Mathf.Repeat(angle + step * 0.5f, step) - step * 0.5f);
                float t = Mathf.Clamp01((toothWidthDeg * 0.5f + 4f - local) / 8f);
                float edge = radius + toothDepth * t * t * (3f - 2f * t);
                float body = d.magnitude - edge;
                return Mathf.Max(body, hole - d.magnitude);
            };
        }

        // ---------- Combinators ----------

        public static Sdf Union(params Sdf[] shapes) => p =>
        {
            float d = float.MaxValue;
            foreach (var s in shapes) d = Mathf.Min(d, s(p));
            return d;
        };

        public static Sdf Intersect(Sdf a, Sdf b) => p => Mathf.Max(a(p), b(p));
        public static Sdf Subtract(Sdf a, Sdf b) => p => Mathf.Max(a(p), -b(p));
        public static Sdf Grow(Sdf a, float amount) => p => a(p) - amount;
        public static Sdf Stroke(Sdf a, float halfWidth) => p => Mathf.Abs(a(p)) - halfWidth;
        public static Sdf Offset(Sdf a, Vector2 by) => p => a(p - by);

        // ---------- Paints ----------

        public static Paint Vertical(Color top, Color bottom, float yTop, float yBottom) => p =>
            Color.Lerp(bottom, top, Mathf.InverseLerp(yBottom, yTop, p.y));

        public static Paint Radial(Color inner, Color outer, Vector2 c, float radius) => p =>
            Color.Lerp(inner, outer, Mathf.Clamp01((p - c).magnitude / radius));

        public static Color Hex(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c))
                throw new ArgumentException($"Bad colour '{hex}'");
            c.a = alpha;
            return c;
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
