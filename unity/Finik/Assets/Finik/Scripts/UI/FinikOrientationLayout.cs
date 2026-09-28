using System;
using UnityEngine;

namespace Finik.UI
{
    [Serializable]
    public struct FinikRectLayout
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;

        public static FinikRectLayout Capture(RectTransform rect) => new()
        {
            anchorMin = rect.anchorMin,
            anchorMax = rect.anchorMax,
            pivot = rect.pivot,
            anchoredPosition = rect.anchoredPosition,
            sizeDelta = rect.sizeDelta
        };

        public void ApplyTo(RectTransform rect)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        /// <summary>Layout anchored to a single corner/edge point of the parent.</summary>
        public static FinikRectLayout At(Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) => new()
        {
            anchorMin = anchor,
            anchorMax = anchor,
            pivot = pivot,
            anchoredPosition = position,
            sizeDelta = size
        };
    }

    /// <summary>
    /// Holds a landscape and a portrait placement for one HUD element and switches between them
    /// whenever the root canvas changes aspect. Use the context menu to capture hand-tuned placements.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class FinikOrientationLayout : MonoBehaviour
    {
        [SerializeField] FinikRectLayout landscape;
        [SerializeField] FinikRectLayout portrait;

        RectTransform rect;
        int appliedOrientation = -1;

        public void Configure(FinikRectLayout landscapeLayout, FinikRectLayout portraitLayout)
        {
            landscape = landscapeLayout;
            portrait = portraitLayout;
            appliedOrientation = -1;
            LateUpdate();
        }

        public void AddHeight(float height)
        {
            landscape.sizeDelta.y += height;
            portrait.sizeDelta.y += height;
            appliedOrientation = -1;
            LateUpdate();
        }

        public void GetHeights(out float landscapeHeight, out float portraitHeight)
        {
            landscapeHeight = landscape.sizeDelta.y;
            portraitHeight = portrait.sizeDelta.y;
        }

        public void SetHeights(float landscapeHeight, float portraitHeight)
        {
            landscape.sizeDelta.y = landscapeHeight;
            portrait.sizeDelta.y = portraitHeight;
            appliedOrientation = -1;
            LateUpdate();
        }

        // Apply at once: a panel that was hidden during a rotation would otherwise show its old
        // placement for a frame, until LateUpdate catches up.
        void OnEnable()
        {
            appliedOrientation = -1;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (!rect) rect = (RectTransform)transform;
            int orientation = FinikScreenOrientation.IsPortrait(rect) ? 1 : 0;
            if (orientation == appliedOrientation) return;
            appliedOrientation = orientation;
            (orientation == 1 ? portrait : landscape).ApplyTo(rect);
        }

        [ContextMenu("Capture current placement as Landscape")]
        void CaptureLandscape() => landscape = FinikRectLayout.Capture((RectTransform)transform);

        [ContextMenu("Capture current placement as Portrait")]
        void CapturePortrait() => portrait = FinikRectLayout.Capture((RectTransform)transform);
    }

    public static class FinikScreenOrientation
    {
        /// <summary>
        /// Uses the root canvas rect rather than Screen.width/height: in edit mode that tracks the
        /// Game view, so both layouts can be previewed without entering Play mode.
        /// </summary>
        public static bool IsPortrait(Component anyChild)
        {
            var canvas = anyChild.GetComponentInParent<Canvas>();
            if (canvas) canvas = canvas.rootCanvas;
            if (canvas)
            {
                Rect r = ((RectTransform)canvas.transform).rect;
                if (r.width > 1f && r.height > 1f) return r.width < r.height;
            }
            return Screen.width < Screen.height;
        }
    }
}
