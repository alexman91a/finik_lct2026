using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Keeps a button's tap area at least <see cref="Minimum"/> canvas units on each axis without
    /// touching its art or its layout: Graphic.raycastPadding takes negative insets that push the
    /// raycast rectangle outwards, so a 60x60 «+» still draws at 60x60 but answers a 132x132 tap.
    ///
    /// Android asks for 48 dp and iOS for 44 pt. The canvas is 1080 units wide in portrait and a
    /// phone is about 393 dp wide, so 48 dp is roughly 132 units. Where two padded areas overlap the
    /// one later in the hierarchy takes the tap, which is why the HUD builds its widgets in reading
    /// order.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Graphic))]
    public sealed class FinikTapTarget : MonoBehaviour
    {
        public const float Minimum = 132f;

        [SerializeField, Tooltip("Минимальная сторона зоны нажатия в юнитах канваса (132 ≈ 48 dp).")]
        float minimum = Minimum;

        Graphic graphic;

        /// <summary>The rect this graphic actually answers taps in, in its own local units.</summary>
        public static Rect HitRect(Graphic target)
        {
            var rect = ((RectTransform)target.transform).rect;
            var padding = target.raycastPadding;
            return Rect.MinMaxRect(rect.xMin + padding.x, rect.yMin + padding.y, rect.xMax - padding.z, rect.yMax - padding.w);
        }

        void OnEnable()
        {
            graphic = GetComponent<Graphic>();
            Apply();
        }

        // Fires whenever a layout group, an anchor change or a rotation resizes this rect.
        void OnRectTransformDimensionsChange() => Apply();

        void Apply()
        {
            if (!graphic) graphic = GetComponent<Graphic>();
            if (!graphic) return;
            var size = ((RectTransform)transform).rect.size;
            float x = Mathf.Max(0f, (minimum - size.x) * 0.5f);
            float y = Mathf.Max(0f, (minimum - size.y) * 0.5f);
            var padding = new Vector4(-x, -y, -x, -y);
            if (graphic.raycastPadding != padding) graphic.raycastPadding = padding;
        }
    }
}
