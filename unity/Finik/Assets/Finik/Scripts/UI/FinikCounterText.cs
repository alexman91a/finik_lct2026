using System.Globalization;
using TMPro;
using UnityEngine;

namespace Finik.UI
{
    /// <summary>Number label that rolls towards its new value and punches an icon on gains.</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FinikCounterText : MonoBehaviour
    {
        static readonly NumberFormatInfo GroupedFormat = new()
        {
            NumberGroupSeparator = " ",
            NumberGroupSizes = new[] { 3 }
        };

        [SerializeField] FinikPressFeedback punchOnGain;
        [SerializeField] float rollDuration = 0.7f;
        [SerializeField] Color gainFlash = new(1f, 0.93f, 0.45f, 1f);

        TMP_Text label;
        Color baseColor;
        int target;
        float from;
        float shown;
        float rollStart = -1f;

        public int Value => target;

        void Awake()
        {
            label = GetComponent<TMP_Text>();
            baseColor = label.color;
        }

        public void SetValue(int value, bool animate = true)
        {
            if (!label) Awake();
            if (!animate || !Application.isPlaying)
            {
                target = value;
                shown = value;
                rollStart = -1f;
                Render(value);
                return;
            }

            if (value == target) return;
            if (value > target && punchOnGain) punchOnGain.Punch();
            from = shown;
            target = value;
            rollStart = Time.unscaledTime;
        }

        void Update()
        {
            if (rollStart < 0f) return;
            float t = (Time.unscaledTime - rollStart) / rollDuration;
            shown = Mathf.Lerp(from, target, FinikUiMotion.EaseOutCubic(t));
            Render(Mathf.RoundToInt(shown));
            label.color = target > from ? Color.Lerp(gainFlash, baseColor, t) : baseColor;
            if (t >= 1f)
            {
                shown = target;
                rollStart = -1f;
                label.color = baseColor;
            }
        }

        void Render(int value) => label.text = value.ToString("#,0", GroupedFormat);
    }
}
