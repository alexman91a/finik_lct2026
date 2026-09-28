using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Lays its children out in as many columns as the width allows (up to <see cref="maxColumns"/>),
    /// each child stacked under the previous one in its column. Children alternate between columns in
    /// hierarchy order — first left, second right, third left… — so a builder decides the pairing.
    ///
    /// On a phone held upright that is one column, like a plain vertical list; on a landscape phone or
    /// a foldable the same menu becomes two columns and fits on one screen instead of scrolling.
    /// </summary>
    [ExecuteAlways]
    public sealed class FinikFlowColumns : LayoutGroup
    {
        [SerializeField, Min(100f)] float minColumnWidth = 820f;
        [SerializeField, Min(1)] int maxColumns = 2;
        [SerializeField] float spacing = 16f;

        int columns = 1;
        float columnWidth;

        public int Columns => columns;

        public void Configure(float narrowest, int most, float gap)
        {
            minColumnWidth = narrowest;
            maxColumns = most;
            spacing = gap;
            SetDirty();
        }

        int ColumnsFor(float width) =>
            Mathf.Clamp(Mathf.FloorToInt((width + spacing) / (minColumnWidth + spacing)), 1, Mathf.Max(1, maxColumns));

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float width = padding.horizontal + minColumnWidth;
            SetLayoutInputForAxis(0f, LayoutUtility.DefaultMaxSize, width, 1f, 0);
        }

        public override void SetLayoutHorizontal()
        {
            float inner = rectTransform.rect.width - padding.horizontal;
            // Never more columns than there are children: a lone section spans the page.
            columns = Mathf.Min(ColumnsFor(inner), Mathf.Max(1, rectChildren.Count));
            columnWidth = (inner - spacing * (columns - 1)) / columns;
            for (int i = 0; i < rectChildren.Count; i++)
                SetChildAlongAxis(rectChildren[i], 0, padding.left + i % columns * (columnWidth + spacing), columnWidth);
        }

        public override void CalculateLayoutInputVertical()
        {
            float tallest = 0f;
            var heights = new float[columns];
            for (int i = 0; i < rectChildren.Count; i++)
            {
                int column = i % columns;
                if (heights[column] > 0f) heights[column] += spacing;
                heights[column] += LayoutUtility.GetPreferredHeight(rectChildren[i]);
                tallest = Mathf.Max(tallest, heights[column]);
            }
            float total = tallest + padding.vertical;
            SetLayoutInputForAxis(total, LayoutUtility.DefaultMaxSize, total, 0f, 1);
        }

        public override void SetLayoutVertical()
        {
            var tops = new float[columns];
            for (int i = 0; i < rectChildren.Count; i++)
            {
                int column = i % columns;
                var child = rectChildren[i];
                float height = LayoutUtility.GetPreferredHeight(child);
                SetChildAlongAxis(child, 1, padding.top + tops[column], height);
                tops[column] += height + spacing;
            }
        }
    }
}
