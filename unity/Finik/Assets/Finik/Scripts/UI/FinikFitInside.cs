using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// Shrinks this rect until it fits its parent, and never grows it past its designed size. The
    /// screen cards are built at a fixed height, and the parent here is the safe area: on a notched
    /// phone in landscape it is about 95% of the screen, which is enough to push a tall card off the
    /// edge. Scaling keeps the card's proportions and its hand-tuned spacing, where clamping the
    /// height would only move the overflow inside the card.
    ///
    /// The scale is worked out from the rect sizes, which localScale does not affect, so it settles in
    /// one pass and cannot oscillate.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class FinikFitInside : MonoBehaviour
    {
        [SerializeField, Tooltip("Отступ от краёв родителя, который оставляем свободным (x, y).")]
        Vector2 margin = new(0f, 24f);

        RectTransform rect;

        public void Configure(Vector2 freeMargin)
        {
            margin = freeMargin;
            Apply();
        }

        void OnEnable()
        {
            rect = (RectTransform)transform;
            Apply();
        }

        // The orientation layout resizes this rect on rotation; catch it in the same frame.
        void OnRectTransformDimensionsChange() => Apply();

        void LateUpdate() => Apply();

        void Apply()
        {
            if (!rect) rect = (RectTransform)transform;
            if (rect.parent is not RectTransform parent) return;
            var size = rect.rect.size;
            var available = parent.rect.size - margin;
            if (size.x <= 1f || size.y <= 1f || available.x <= 1f || available.y <= 1f) return;

            float fit = Mathf.Min(1f, available.x / size.x, available.y / size.y);
            var scale = new Vector3(fit, fit, 1f);
            if ((rect.localScale - scale).sqrMagnitude > 1e-8f) rect.localScale = scale;
        }
    }
}
