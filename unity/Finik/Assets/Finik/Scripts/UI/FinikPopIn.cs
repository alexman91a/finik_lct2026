using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// Staggered pop-in (scale + fade) when the HUD first appears in Play mode. Only touches this
    /// transform's scale and the CanvasGroup alpha: positions stay owned by FinikOrientationLayout,
    /// and press feedback lives on child transforms.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FinikPopIn : MonoBehaviour
    {
        [SerializeField, Min(0f)] float delay;
        [SerializeField, Min(0.05f)] float duration = 0.5f;
        [SerializeField, Range(0f, 1f)] float startScale = 0.55f;

        CanvasGroup group;
        float startTime;
        bool running;

        public void Configure(float delaySeconds, float fromScale = 0.55f)
        {
            delay = delaySeconds;
            startScale = fromScale;
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            group = GetComponent<CanvasGroup>();
            startTime = Time.unscaledTime + delay;
            running = true;
            Step(0f);
        }

        void OnDisable()
        {
            if (running) Step(1f);
            running = false;
        }

        void Update()
        {
            if (!running) return;
            float t = (Time.unscaledTime - startTime) / duration;
            if (t < 0f) return;
            Step(t);
            if (t >= 1f) running = false;
        }

        void Step(float t)
        {
            group.alpha = Mathf.Clamp01(t * 3f);
            float s = Mathf.LerpUnclamped(startScale, 1f, FinikUiMotion.EaseOutBack(t, 2.2f));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
