using System.Collections.Generic;
using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// Marks a HUD widget a room hint must never cover.
    ///
    /// Instead of ordering the canvas so hints slide under the HUD — which still leaves them hidden —
    /// <see cref="FinikRoomBubble"/> reads every obstacle each frame and pushes itself clear of them,
    /// the same way two hints push each other apart. Put one on each thing that must stay readable:
    /// the pills, the rings, the dock, the cards.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class FinikHudObstacle : MonoBehaviour
    {
        [Tooltip("Extra clearance around the widget, in canvas units.")]
        [SerializeField, Min(0f)] float padding = 12f;

        static readonly List<FinikHudObstacle> Active = new();

        public static IReadOnlyList<FinikHudObstacle> All => Active;

        public float Padding => padding;

        // No cached field: `??=` does not see a destroyed Unity object, and after a script reload the
        // editor restored a cache pointing at a transform that no longer existed. TryBox then failed
        // for every HUD widget and the room hints walked straight over them.
        public RectTransform Rect => (RectTransform)transform;

        void OnEnable() => Active.Add(this);

        void OnDisable() => Active.Remove(this);

        /// <summary>
        /// The widget's box in <paramref name="space"/>'s local coordinates, relative to that rect's
        /// centre — the space room hints are positioned in — grown by <see cref="Padding"/>.
        /// Empty when the widget is hidden or has no area.
        /// </summary>
        public bool TryBox(RectTransform space, out UnityEngine.Rect box)
        {
            box = default;
            if (!space || !isActiveAndEnabled || !Rect) return false;
            var size = Rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return false;

            var corners = new Vector3[4];
            Rect.GetWorldCorners(corners);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in corners)
            {
                var local = (Vector2)space.InverseTransformPoint(corner) - space.rect.center;
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            box = UnityEngine.Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
            return box.width > 0f && box.height > 0f;
        }
    }
}
