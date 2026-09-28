using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// One half of the catch basket, cut along the front edge of its rim. The back half (back rim and the
    /// inside) is drawn under a falling coin, the front half (front rim and the woven wall) over it, so a
    /// caught coin visibly drops into the basket instead of sliding behind the whole picture. Both halves
    /// share the cut's vertices, so together they are exactly the plain sprite, with no seam and no edge
    /// drawn twice.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FinikBasketLayer : MaskableGraphic
    {
        public enum Part { Back, Front }

        // The opening of game_basket.png, measured on the 384 px picture: an ellipse centred at
        // (187, 128.5) from the top-left corner with semi-axes 117 × 23.5. Everything is in sprite
        // units: 0..1 across, 0..1 up.
        public const float MouthU = 187f / 384f;
        public const float MouthV = 1f - 128.5f / 384f;
        public const float MouthHalfWidth = 117f / 384f;
        public const float MouthHalfDepth = 23.5f / 384f;
        /// <summary>Half the width of the rim's outside, handles not counted.</summary>
        public const float RimHalfWidth = 153f / 384f;
        /// <summary>The cut runs a hair inside the front rim, so a coin behind it never shows a dark sliver of the inside.</summary>
        const float EdgeMargin = 1.5f / 384f;
        /// <summary>Enough columns that the rim's curve reads as a curve even on a tablet.</summary>
        const int Segments = 96;

        /// <summary>Lowest point of the cut: a coin whose top is under it has fully sunk into the basket.</summary>
        public const float FrontEdgeV = MouthV - MouthHalfDepth - EdgeMargin;

        [SerializeField] Sprite sprite;
        [SerializeField] Part part;

        public Sprite Sprite
        {
            get => sprite;
            set
            {
                if (sprite == value) return;
                sprite = value;
                SetAllDirty();
            }
        }

        public Part Half
        {
            get => part;
            set
            {
                if (part == value) return;
                part = value;
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture => sprite ? sprite.texture : s_WhiteTexture;

        /// <summary>
        /// Height (0 at the bottom, 1 at the top) where the cut crosses column <paramref name="u"/>: the
        /// front edge of the rim across the opening, level with the middle of the mouth beyond its ends,
        /// so the rim's sides and the handles stay behind whatever is inside.
        /// </summary>
        public static float CutV(float u)
        {
            float d = (u - MouthU) / MouthHalfWidth;
            float depth = d * d >= 1f ? 0f : Mathf.Sqrt(1f - d * d);
            return MouthV - (MouthHalfDepth + EdgeMargin) * depth;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!sprite) return;
            var rect = GetPixelAdjustedRect();
            // x, y: the sprite's lower-left corner in its texture; z, w: the upper-right one.
            var uv = DataUtility.GetOuterUV(sprite);
            var tint = (Color32)color;
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                float cut = CutV(u);
                float low = part == Part.Front ? 0f : cut;
                float high = part == Part.Front ? cut : 1f;
                float x = rect.xMin + u * rect.width;
                float tu = Mathf.Lerp(uv.x, uv.z, u);
                vh.AddVert(new Vector3(x, rect.yMin + low * rect.height), tint, new Vector2(tu, Mathf.Lerp(uv.y, uv.w, low)));
                vh.AddVert(new Vector3(x, rect.yMin + high * rect.height), tint, new Vector2(tu, Mathf.Lerp(uv.y, uv.w, high)));
                if (i == 0) continue;
                int at = i * 2;
                vh.AddTriangle(at - 2, at - 1, at + 1);
                vh.AddTriangle(at + 1, at, at - 2);
            }
        }
    }
}
