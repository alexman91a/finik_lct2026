using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Pill-shaped progress bar. The fill is a sliced sprite whose right anchor moves, so its rounded
    /// caps stay intact at every value (unlike Image.fillAmount, which slices the cap off).
    /// </summary>
    [ExecuteAlways]
    public sealed class FinikFillBar : FinikMeter
    {
        [SerializeField] RectTransform fill;
        [SerializeField] Image fillImage;
        [SerializeField, Range(0f, 1f)] float value = 0.6f;
        [SerializeField] float smoothTime = 0.35f;
        [Header("Low value warning")]
        [SerializeField, Range(0f, 1f)] float lowThreshold = 0f;
        [SerializeField] Color lowPulseColor = new(1f, 0.35f, 0.3f, 1f);

        float shown;
        float velocity;
        Color baseColor = Color.white;
        bool baseColorCaptured;

        public override float Value => value;

        public override void SetValue(float newValue, bool animate = true)
        {
            value = Mathf.Clamp01(newValue);
            if (!animate || !Application.isPlaying)
            {
                shown = value;
                velocity = 0f;
                Apply();
            }
        }

        void OnEnable()
        {
            shown = value;
            CaptureColor();
            Apply();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            shown = value;
            // RectTransform edits are not allowed inside OnValidate; defer them.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this) Apply();
            };
        }
#endif

        void CaptureColor()
        {
            if (baseColorCaptured || !fillImage) return;
            baseColor = fillImage.color;
            baseColorCaptured = true;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            shown = Mathf.SmoothDamp(shown, value, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            Apply();

            if (!fillImage || lowThreshold <= 0f) return;
            CaptureColor();
            if (value < lowThreshold)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                fillImage.color = Color.Lerp(baseColor, lowPulseColor, pulse * 0.8f);
            }
            else
            {
                fillImage.color = baseColor;
            }
        }

        void Apply()
        {
            if (!fill) return;
            var track = (RectTransform)fill.parent;
            float trackWidth = track.rect.width;
            float height = track.rect.height;
            fill.gameObject.SetActive(shown > 0.005f);
            // Never let the pill get narrower than it is tall, otherwise the caps collapse.
            float minFraction = trackWidth > 0f ? Mathf.Clamp01(height / trackWidth) : 0f;
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(Mathf.Lerp(minFraction, 1f, shown), 1f);
        }
    }
}
