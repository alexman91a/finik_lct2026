using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// A menu column exactly as tall as what it holds, and never taller than the screen. Short menus
    /// sit as a compact card in the middle; long ones reach the edges and scroll inside. A fixed-height
    /// card either leaves a slab of empty glass under short content or clips long content away.
    ///
    /// Width is clamped the same way, so a narrow canvas never pushes the card off the sides. Where
    /// there is room for <see cref="wideWidth"/> (a landscape phone, a foldable) the column takes it,
    /// and a <see cref="FinikFlowColumns"/> inside turns the one long list into two short columns.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class FinikFitColumn : MonoBehaviour
    {
        [SerializeField, Tooltip("What the column holds; its preferred height drives the column's height.")]
        RectTransform content;
        [SerializeField, Tooltip("Height the column adds around the content: paddings, header, footer.")]
        float chrome;
        [SerializeField, Tooltip("The column's width on an upright phone.")]
        float maxWidth = 1010f;
        [SerializeField, Tooltip("The column's width once the screen has room for it; equal to maxWidth keeps it narrow.")]
        float wideWidth = 1010f;
        [SerializeField, Tooltip("Room left free around the column inside its parent (x, y).")]
        Vector2 margin = new(32f, 56f);

        RectTransform rect;

        public void Configure(RectTransform holds, float extraHeight, float narrow, float wide, Vector2 freeMargin)
        {
            content = holds;
            chrome = extraHeight;
            maxWidth = narrow;
            wideWidth = Mathf.Max(narrow, wide);
            margin = freeMargin;
            Apply();
        }

        void OnEnable() => Apply();

        void LateUpdate() => Apply();

        /// <summary>
        /// Sizes the column now instead of at the end of the frame: lays the content out first, so a
        /// column shown this very frame (a panel just opened, an audit) already has its real height.
        /// </summary>
        public void Refresh()
        {
            // Twice: the width decides how the text wraps and how many columns the content gets,
            // and only then is its height known.
            for (int pass = 0; pass < 2; pass++)
            {
                if (content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Apply();
            }
            if (content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        /// <summary><see cref="Refresh"/> for every fitted column under <paramref name="root"/>.</summary>
        public static void RefreshUnder(Component root)
        {
            if (!root) return;
            foreach (var column in root.GetComponentsInChildren<FinikFitColumn>(true)) column.Refresh();
        }

        void Apply()
        {
            if (!rect) rect = (RectTransform)transform;
            if (!content || rect.parent is not RectTransform parent) return;
            var available = parent.rect.size - margin;
            if (available.x <= 1f || available.y <= 1f) return;

            float width = available.x >= wideWidth ? wideWidth : Mathf.Min(maxWidth, available.x);
            float height = Mathf.Min(LayoutUtility.GetPreferredHeight(content) + chrome, available.y);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var size = new Vector2(width, Mathf.Max(1f, height));
            if ((rect.sizeDelta - size).sqrMagnitude > 0.25f) rect.sizeDelta = size;
            if (rect.anchoredPosition != Vector2.zero) rect.anchoredPosition = Vector2.zero;
        }
    }
}
