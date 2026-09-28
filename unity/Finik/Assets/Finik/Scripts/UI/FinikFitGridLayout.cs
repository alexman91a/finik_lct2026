using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Grid whose cells always split the available width, with its own column count and row height per
    /// orientation (two cards per row in landscape, one wide row each in portrait). Cell sizes are worked
    /// out inside the layout pass itself, so they can never lag behind an orientation change.
    /// </summary>
    [ExecuteAlways]
    public sealed class FinikFitGridLayout : LayoutGroup
    {
        [SerializeField, Min(1)] int landscapeColumns = 2;
        [SerializeField, Min(1f)] float landscapeRowHeight = 164f;
        [SerializeField, Min(1)] int portraitColumns = 1;
        [SerializeField, Min(1f)] float portraitRowHeight = 104f;
        [SerializeField] Vector2 spacing = new(14f, 14f);

        int appliedOrientation = -1;

        public void Configure(int landscapeCols, float landscapeHeight, int portraitCols, float portraitHeight, Vector2 gap)
        {
            landscapeColumns = Mathf.Max(1, landscapeCols);
            landscapeRowHeight = landscapeHeight;
            portraitColumns = Mathf.Max(1, portraitCols);
            portraitRowHeight = portraitHeight;
            spacing = gap;
            SetDirty();
        }

        bool Portrait => FinikScreenOrientation.IsPortrait(this);
        int Columns => Portrait ? portraitColumns : landscapeColumns;
        float RowHeight => Portrait ? portraitRowHeight : landscapeRowHeight;
        int Rows => (rectChildren.Count + Columns - 1) / Columns;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(padding.horizontal, LayoutUtility.DefaultMaxSize, padding.horizontal, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            int rows = Rows;
            float height = padding.vertical + rows * RowHeight + Mathf.Max(0, rows - 1) * spacing.y;
            SetLayoutInputForAxis(height, height, height, -1f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            int columns = Columns;
            float width = (rectTransform.rect.width - padding.horizontal - spacing.x * (columns - 1)) / columns;
            for (int i = 0; i < rectChildren.Count; i++)
                SetChildAlongAxis(rectChildren[i], 0, padding.left + i % columns * (width + spacing.x), width);
        }

        public override void SetLayoutVertical()
        {
            int columns = Columns;
            float height = RowHeight;
            for (int i = 0; i < rectChildren.Count; i++)
                SetChildAlongAxis(rectChildren[i], 1, padding.top + i / columns * (height + spacing.y), height);
        }

        void Update()
        {
            int orientation = Portrait ? 1 : 0;
            if (orientation == appliedOrientation) return;
            appliedOrientation = orientation;
            SetDirty();
        }
    }
}
