using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>One big candy button of the bottom dock. Selected tabs sit raised, glow and wiggle their icon.</summary>
    public sealed class FinikTabButton : MonoBehaviour
    {
        [SerializeField] FinikHudTab tab;
        [SerializeField] Button button;
        [SerializeField] FinikPressFeedback press;
        [SerializeField] Graphic selectionGlow;
        [SerializeField] Graphic selectionIndicator;
        [SerializeField] TMPro.TMP_Text label;
        [SerializeField] Color labelColor = Color.white;
        [SerializeField] Color selectedLabelColor = new(1f, 0.84f, 0.3f, 1f);
        [SerializeField] FinikIdleMotion iconMotion;
        [SerializeField, Range(1f, 1.2f)] float selectedScale = 1.05f;
        [SerializeField] float selectedLift = 12f;

        RectTransform lifted;
        Vector2 liftBase;

        bool selected;
        float glowAlpha;

        public FinikHudTab Tab => tab;
        public Button Button => button;

        public void Configure(FinikHudTab tabId, Button tabButton, FinikPressFeedback feedback, Graphic glow, FinikIdleMotion motion,
            Graphic indicator, TMPro.TMP_Text caption)
        {
            selectionIndicator = indicator;
            label = caption;
            tab = tabId;
            button = tabButton;
            press = feedback;
            selectionGlow = glow;
            iconMotion = motion;
        }

        public void SetSelected(bool value, bool animate = true)
        {
            if (selected != value && animate && value && press) press.Punch(0.8f);
            selected = value;
            if (press) press.BaseScale = value ? selectedScale : 1f;
            if (iconMotion) iconMotion.enabled = value;
            if (!animate) glowAlpha = value ? 1f : 0f;
            ApplyGlow();
        }

        void Update()
        {
            float goal = selected ? 1f : 0f;
            // Keep ticking while selected so the glow breathes.
            if (!selected && glowAlpha <= 0f) return;
            glowAlpha = Mathf.MoveTowards(glowAlpha, goal, Time.unscaledDeltaTime * 5f);
            ApplyGlow();
        }

        void Awake()
        {
            if (press) lifted = press.Target as RectTransform;
            if (lifted) liftBase = lifted.anchoredPosition;
        }

        void ApplyGlow()
        {
            if (lifted) lifted.anchoredPosition = liftBase + new Vector2(0f, selectedLift * FinikUiMotion.EaseOutBack(glowAlpha));
            if (label) label.color = Color.Lerp(labelColor, selectedLabelColor, glowAlpha);
            if (selectionIndicator)
            {
                Color ic = selectionIndicator.color;
                ic.a = glowAlpha;
                selectionIndicator.color = ic;
                selectionIndicator.rectTransform.localScale = new Vector3(Mathf.Lerp(0.2f, 1f, FinikUiMotion.EaseOutBack(glowAlpha)), 1f, 1f);
            }
            if (!selectionGlow) return;
            Color c = selectionGlow.color;
            c.a = glowAlpha * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f));
            selectionGlow.color = c;
            selectionGlow.enabled = glowAlpha > 0.001f;
        }
    }
}
