using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Food
{
    /// <summary>
    /// One food option: icon, title, trade-off, price and whether the wallet covers it. In landscape it
    /// is a card (title up to two lines, price row at the bottom); in portrait a wide row (one line
    /// each, price on the right). The subtitle takes a second line when the title needs only one;
    /// anything longer ends in an ellipsis, and the catalog length limits keep that from happening.
    /// </summary>
    public sealed class FinikFoodOptionView : MonoBehaviour
    {
        [SerializeField] FinikChoiceItem choice;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] TMP_Text cost;
        [SerializeField] TMP_Text affordability;
        [SerializeField] VerticalLayoutGroup textStack;
        [SerializeField] Color affordableColor = new(0.13f, 0.62f, 0.3f, 1f);
        [SerializeField] Color shortColor = new(0.86f, 0.24f, 0.28f, 1f);
        [Tooltip("«По карману» on every row is noise: on, the line only appears when it is not. Off shows both.")]
        [SerializeField] bool affordabilityOnlyWhenShort;
        [Tooltip("Faded while the wallet cannot cover it. Left empty the row never fades.")]
        [SerializeField] CanvasGroup outOfReach;
        [SerializeField, Range(0.3f, 1f)] float outOfReachAlpha = 0.55f;

        int fittedOrientation = -1;
        float fittedWidth = -1f;

        public FinikChoiceItem Choice => choice;
        public string PlanId { get; private set; }

        public void Configure(FinikChoiceItem item, Image iconImage, TMP_Text titleText, TMP_Text subtitleText, TMP_Text costText, TMP_Text affordText, VerticalLayoutGroup stack)
        {
            choice = item;
            icon = iconImage;
            title = titleText;
            subtitle = subtitleText;
            cost = costText;
            affordability = affordText;
            textStack = stack;
        }

        public void Show(FinikFoodPlan plan, Sprite sprite, bool affordable)
        {
            PlanId = plan.Id;
            if (icon) icon.sprite = sprite;
            if (title) title.text = FinikTypography.Fix(plan.Title);
            if (subtitle) subtitle.text = FinikTypography.Fix(plan.Subtitle);
            if (cost) cost.text = plan.TotalCost.ToString();
            SetAffordable(affordable);
            fittedOrientation = -1;
            Fit();
        }

        public void SetAffordable(bool affordable)
        {
            // Quiet, not hidden: the option stays tappable so the screen can explain the gap.
            if (outOfReach) outOfReach.alpha = affordable ? 1f : outOfReachAlpha;
            if (!affordability) return;
            affordability.text = affordable ? "по карману" : "не хватает";
            affordability.color = affordable ? affordableColor : shortColor;
            if (affordabilityOnlyWhenShort) affordability.gameObject.SetActive(!affordable);
        }

        public void SetInteractable(bool value)
        {
            if (choice && choice.Button) choice.Button.interactable = value;
        }

        // Rotation and the layout pass both change the text width; refit whenever it moves.
        void LateUpdate() => Fit();

        void Fit()
        {
            if (!title) return;
            int orientation = FinikScreenOrientation.IsPortrait(this) ? 1 : 0;
            // The stack's own rect is placed by FinikOrientationLayout, so its width is right before
            // the layout pass has resized the texts inside it.
            var box = textStack ? (RectTransform)textStack.transform : title.rectTransform;
            float width = box.rect.width - (textStack ? textStack.padding.horizontal : 0);
            if (width <= 0f || (orientation == fittedOrientation && Mathf.Approximately(width, fittedWidth))) return;
            fittedOrientation = orientation;
            fittedWidth = width;

            bool row = orientation == 1;
            if (textStack) textStack.childAlignment = row ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft;
            if (affordability) affordability.alignment = row ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;

            // Line limits go through the wrapping mode, not maxVisibleLines: TextMeshPro reports its
            // preferred height for every line regardless, and the text stack sizes by preferred height.
            title.textWrappingMode = row ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            // At the readable 24-unit floor, several trade-off captions no longer fit on one line.
            // Landscape cards have enough height for the second line; only the wide portrait row
            // intentionally keeps the subtitle to one line.
            if (subtitle) subtitle.textWrappingMode = row ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            if (textStack) LayoutRebuilder.MarkLayoutForRebuild(box);
        }

        static int Lines(TMP_Text text, float width)
        {
            float single = text.GetPreferredValues("Ж", width, 0f).y;
            float full = text.GetPreferredValues(text.text, width, 0f).y;
            return single <= 0f ? 1 : Mathf.Max(1, Mathf.RoundToInt(full / single));
        }
    }
}
