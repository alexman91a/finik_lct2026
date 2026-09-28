using UnityEngine;

namespace Finik.UI
{
    /// <summary>Gentle looping bob / sway / breathe for decorative icons.</summary>
    public sealed class FinikIdleMotion : MonoBehaviour
    {
        [SerializeField] float bobAmplitude = 4f;
        [SerializeField] float swayDegrees = 0f;
        [SerializeField, Range(0f, 0.3f)] float breathe = 0f;
        [SerializeField] float speed = 1.6f;
        [SerializeField] float phase;

        RectTransform rect;
        Vector2 basePosition;

        public void Configure(float bob, float sway, float breatheAmount, float speedHz, float phaseOffset)
        {
            bobAmplitude = bob;
            swayDegrees = sway;
            breathe = breatheAmount;
            speed = speedHz;
            phase = phaseOffset;
        }

        void OnEnable()
        {
            rect = (RectTransform)transform;
            basePosition = rect.anchoredPosition;
        }

        void OnDisable()
        {
            if (!rect) return;
            if (bobAmplitude != 0f) rect.anchoredPosition = basePosition;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        void Update()
        {
            float t = Time.unscaledTime * speed + phase;
            // Leave position alone when not bobbing, so layout groups stay in charge of it.
            if (bobAmplitude != 0f) rect.anchoredPosition = basePosition + new Vector2(0f, Mathf.Sin(t) * bobAmplitude);
            if (swayDegrees != 0f) rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.8f) * swayDegrees);
            if (breathe != 0f)
            {
                float s = 1f + Mathf.Sin(t * 1.3f) * breathe;
                rect.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
