using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Food
{
    /// <summary>
    /// The outcome card after paying for food: verdict, what happened and why, what changed
    /// (coins, needs, XP) and Finik's comment. Shown instead of the choice card.
    /// </summary>
    public sealed class FinikFoodResultView : MonoBehaviour
    {
        [SerializeField] FinikScreenPanel panel;
        [SerializeField] Image verdictBackground;
        [SerializeField] TMP_Text verdict;
        [SerializeField] Image dishIcon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text explanation;
        [SerializeField] TMP_Text coinsDelta;
        [SerializeField] TMP_Text foodDelta;
        [SerializeField] TMP_Text moodDelta;
        [SerializeField] GameObject xpChip;
        [SerializeField] TMP_Text xpDelta;
        [SerializeField] TMP_Text petLine;
        [SerializeField] Image petAvatar;
        [SerializeField] Button closeButton;
        [SerializeField] FinikConfettiBurst confetti;

        [SerializeField] Color smartColor = new(0.18f, 0.74f, 0.35f, 1f);
        [SerializeField] Color balancedColor = new(0.25f, 0.62f, 1f, 1f);
        [SerializeField] Color treatColor = new(1f, 0.6f, 0.18f, 1f);
        [SerializeField] Color trapColor = new(0.93f, 0.33f, 0.45f, 1f);
        [SerializeField] Color gainColor = new(0.13f, 0.62f, 0.3f, 1f);
        [SerializeField] Color lossColor = new(0.86f, 0.24f, 0.28f, 1f);
        [SerializeField] Color neutralColor = new(0.36f, 0.41f, 0.62f, 1f);

        public FinikScreenPanel Panel => panel;
        public Button CloseButton => closeButton;

        public readonly struct Outcome
        {
            public readonly FinikFoodPlan plan;
            public readonly string message;
            public readonly int coinsSpent;
            public readonly FinikNeeds needsGained;
            public readonly int xpGained;

            public Outcome(FinikFoodPlan plan, string message, int coinsSpent, FinikNeeds needsGained, int xpGained)
            {
                this.plan = plan;
                this.message = message;
                this.coinsSpent = coinsSpent;
                this.needsGained = needsGained;
                this.xpGained = xpGained;
            }
        }

        public void Show(Outcome outcome, Sprite icon, Sprite avatar)
        {
            var plan = outcome.plan;
            var color = plan.Vibe switch
            {
                FinikFoodVibe.Smart => smartColor,
                FinikFoodVibe.Balanced => balancedColor,
                FinikFoodVibe.Treat => treatColor,
                _ => trapColor
            };
            if (verdictBackground) verdictBackground.color = color;
            if (verdict) verdict.text = plan.Vibe switch
            {
                FinikFoodVibe.Smart => "Умный выбор",
                FinikFoodVibe.Balanced => "Нормальный вариант",
                FinikFoodVibe.Treat => "Можно иногда",
                _ => "Был подвох"
            };
            if (dishIcon) dishIcon.sprite = icon;
            if (title) title.text = FinikTypography.Fix(plan.Title);
            if (explanation) explanation.text = FinikTypography.Fix(outcome.message);

            SetDelta(coinsDelta, -outcome.coinsSpent);
            SetDelta(foodDelta, outcome.needsGained.food);
            SetDelta(moodDelta, outcome.needsGained.mood);
            if (xpChip) xpChip.SetActive(outcome.xpGained > 0);
            SetDelta(xpDelta, outcome.xpGained);

            if (petLine) petLine.text = FinikTypography.Fix($"«{PetComment(plan.Vibe)}»");
            if (petAvatar && avatar) petAvatar.sprite = avatar;
            panel.Show();
            if (confetti && plan.Vibe == FinikFoodVibe.Smart) confetti.Burst(Vector2.zero);
        }

        void SetDelta(TMP_Text text, float value)
        {
            if (!text) return;
            int rounded = Mathf.RoundToInt(value);
            text.text = rounded > 0 ? $"+{rounded}" : rounded < 0 ? $"–{-rounded}" : "0";
            text.color = rounded > 0 ? gainColor : rounded < 0 ? lossColor : neutralColor;
        }

        static string PetComment(FinikFoodVibe vibe) => vibe switch
        {
            FinikFoodVibe.Smart => "Вот это план! И сытно, и монеты целы.",
            FinikFoodVibe.Balanced => "Нормально поели. Главное — видеть полную цену.",
            FinikFoodVibe.Treat => "Вкусно! Но каждый день так не получится.",
            _ => "Хм… кажется, нас немного развели."
        };
    }
}
