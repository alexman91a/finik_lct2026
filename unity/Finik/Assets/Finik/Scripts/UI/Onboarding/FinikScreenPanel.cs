using UnityEngine;

namespace Finik.UI.Onboarding
{
    /// <summary>
    /// One onboarding screen. Shows with a springy scale + slide + fade and hides by fading out, and
    /// blocks input while hidden or animating out.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FinikScreenPanel : MonoBehaviour
    {
        [SerializeField] float showSeconds = 0.45f;
        [SerializeField] float hideSeconds = 0.22f;
        [SerializeField] Vector2 slide = new(0f, -60f);

        CanvasGroup group;
        RectTransform rect;
        float t;
        bool showing;
        bool visible;

        public bool IsVisible => visible;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            // The panel root is a full-screen stretch; the slide offsets it, never its children,
            // whose placement belongs to FinikOrientationLayout.
            rect = (RectTransform)transform;
        }

        public void Show(bool instant = false)
        {
            if (!group) Awake();
            gameObject.SetActive(true);
            visible = true;
            showing = true;
            t = instant ? 1f : 0f;
            group.interactable = true;
            group.blocksRaycasts = true;
            Apply();
        }

        public void Hide(bool instant = false)
        {
            if (!group) Awake();
            if (!gameObject.activeSelf) return;
            visible = false;
            showing = false;
            t = instant ? 1f : 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            Apply();
        }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / (showing ? showSeconds : hideSeconds));
            Apply();
        }

        void Apply()
        {
            if (showing)
            {
                float e = FinikUiMotion.EaseOutBack(t, 1.4f);
                group.alpha = Mathf.Clamp01(t * 2.5f);
                transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.94f, 1f, e);
                rect.anchoredPosition = Vector2.LerpUnclamped(slide, Vector2.zero, e);
            }
            else
            {
                group.alpha = 1f - t;
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.97f, t);
                if (t >= 1f)
                {
                    rect.anchoredPosition = Vector2.zero;
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
