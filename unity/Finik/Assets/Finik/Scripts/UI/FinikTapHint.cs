using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// A pointing hand that taps a target again and again, a ripple spreading under the fingertip, for
    /// controls a child would not guess are tappable. It never takes a tap itself. The fingertip is
    /// re-placed on the target every frame, so layout, rotation and the target's own press spring can't
    /// leave it behind.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class FinikTapHint : MonoBehaviour
    {
        [SerializeField, Tooltip("Moves to the tapped point; the hand and the ripple are its children.")]
        RectTransform tip;
        [SerializeField, Tooltip("The hand image, pivoted at its fingertip.")]
        RectTransform hand;
        [SerializeField] RectTransform ripple;
        [SerializeField, Tooltip("Where on the target the fingertip lands, 0..1 of its rect.")]
        Vector2 point = new(0.84f, 0.72f);
        [SerializeField, Tooltip("Where the hand rests between taps, in the tip's units.")]
        Vector2 lift = new(30f, -30f);
        [SerializeField, Min(0.3f)] float period = 1.5f;
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.2f;

        CanvasGroup group;
        CanvasGroup rippleGroup;
        RectTransform target;
        float shownAt;
        bool visible;

        public bool IsVisible => visible;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            if (ripple && !ripple.TryGetComponent(out rippleGroup)) rippleGroup = ripple.gameObject.AddComponent<CanvasGroup>();
        }

        void OnEnable()
        {
            if (!group) Awake();
            if (!visible) group.alpha = 0f;
        }

        /// <summary>Starts tapping <paramref name="newTarget"/>; the loop restarts, so the first tap lands right away.</summary>
        public void Show(RectTransform newTarget)
        {
            if (!newTarget) { Hide(); return; }
            if (!group) Awake();
            gameObject.SetActive(true);
            target = newTarget;
            visible = true;
            shownAt = Time.unscaledTime;
            Follow();
            Animate(0f);
        }

        public void Hide(bool instant = false)
        {
            visible = false;
            if (instant && group) group.alpha = 0f;
        }

        void LateUpdate()
        {
            float goal = visible && target && target.gameObject.activeInHierarchy ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, goal, Time.unscaledDeltaTime / fadeSeconds);
            if (group.alpha <= 0f) return;
            Follow();
            Animate(Time.unscaledTime - shownAt);
        }

        void Follow()
        {
            if (!target || !tip) return;
            var r = target.rect;
            tip.position = target.TransformPoint(new Vector3(r.x + r.width * point.x, r.y + r.height * point.y, 0f));
        }

        /// <summary>
        /// One cycle: the hand swoops in from its resting spot, presses (the ripple starts there), holds
        /// for a beat and drifts back. The first appearance also pops the hand in.
        /// </summary>
        void Animate(float elapsed)
        {
            float p = Mathf.Repeat(elapsed / period, 1f);
            float away = p < 0.4f
                ? 1f - FinikUiMotion.EaseOutCubic(p / 0.4f)
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, p));
            float press = p is > 0.36f and < 0.56f ? Mathf.Sin(Mathf.InverseLerp(0.36f, 0.56f, p) * Mathf.PI) : 0f;
            float pop = Mathf.Lerp(0.6f, 1f, FinikUiMotion.EaseOutBack(Mathf.Clamp01(elapsed / 0.3f)));

            if (hand)
            {
                hand.anchoredPosition = lift * away;
                float s = pop * (1f - 0.14f * press);
                hand.localScale = new Vector3(s, s, 1f);
            }

            if (!ripple) return;
            float r = Mathf.InverseLerp(0.42f, 0.95f, p);
            float spread = FinikUiMotion.EaseOutCubic(r);
            float scale = Mathf.Lerp(0.35f, 1.25f, spread);
            ripple.localScale = new Vector3(scale, scale, 1f);
            if (rippleGroup) rippleGroup.alpha = p < 0.42f ? 0f : 1f - r;
        }
    }
}
