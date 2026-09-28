using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Circular need gauge: a radial-filled ring around an icon. Animates towards new values, nudges
    /// the icon when the value rises and pulses red when it drops below <see cref="lowThreshold"/>.
    /// </summary>
    public sealed class FinikRingGauge : FinikMeter, IPointerClickHandler
    {
        [SerializeField] Image ring;
        [SerializeField] FinikPressFeedback iconPunch;
        [SerializeField, Range(0f, 1f)] float value = 0.6f;
        [SerializeField] float smoothTime = 0.4f;
        [SerializeField, Range(0f, 1f)] float lowThreshold = 0.25f;
        [SerializeField] Color lowPulseColor = new(1f, 0.3f, 0.28f, 1f);
        [SerializeField] TMP_Text percentLabel;
        [SerializeField] float percentHold = 1.4f;

        float percentShownAt = -10f;
        // The percentage pops out beside the ring, where a card may sit. Sibling order cannot lift it
        // there — the rings and the cards are separate widgets — so while it shows it gets its own
        // sorting order, just above the HUD canvas and still below the room screens.
        const int PercentSortingOrder = 21;
        Canvas percentCanvas;

        float shown;
        float velocity;
        Color baseColor = Color.white;
        bool colorCaptured;

        public override float Value => value;

        public void Configure(Image ringImage, FinikPressFeedback punch, float low, TMP_Text percent)
        {
            percentLabel = percent;
            ring = ringImage;
            iconPunch = punch;
            lowThreshold = low;
        }

        public override void SetValue(float newValue, bool animate = true)
        {
            newValue = Mathf.Clamp01(newValue);
            if (animate && Application.isPlaying && newValue > value + 0.01f && iconPunch) iconPunch.Punch(0.7f);
            value = newValue;
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
            Capture();
            Apply();
        }

        void Capture()
        {
            if (colorCaptured || !ring) return;
            baseColor = ring.color;
            colorCaptured = true;
        }

        public void OnPointerClick(PointerEventData eventData) => percentShownAt = Time.unscaledTime;

        void OnDisable() => Lower();

        void Raise()
        {
            if (!percentLabel) return;
            if (!percentCanvas)
            {
                percentCanvas = percentLabel.GetComponent<Canvas>();
                if (!percentCanvas) percentCanvas = percentLabel.gameObject.AddComponent<Canvas>();
            }
            percentCanvas.overrideSorting = true;
            percentCanvas.sortingOrder = PercentSortingOrder;
        }

        void Lower()
        {
            if (percentCanvas) percentCanvas.overrideSorting = false;
        }

        void Update()
        {
            if (!Application.isPlaying || !ring) return;
            UpdatePercent();
            shown = Mathf.SmoothDamp(shown, value, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            Apply();
            Capture();
            if (value < lowThreshold)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                ring.color = Color.Lerp(baseColor, lowPulseColor, pulse * 0.85f);
            }
            else
            {
                ring.color = baseColor;
            }
        }

        void UpdatePercent()
        {
            if (!percentLabel) return;
            float t = Time.unscaledTime - percentShownAt;
            float alpha = t < 0.15f ? t / 0.15f : Mathf.Clamp01(1f - (t - percentHold) / 0.3f);
            percentLabel.alpha = alpha;
            if (alpha > 0f)
            {
                percentLabel.text = Mathf.RoundToInt(shown * 100f) + "%";
                Raise();
            }
            else Lower();
        }

        void Apply()
        {
            if (ring) ring.fillAmount = shown;
        }
    }
}
