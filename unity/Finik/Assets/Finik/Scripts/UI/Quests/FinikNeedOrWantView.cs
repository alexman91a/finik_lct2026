using System;
using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;

namespace Finik.UI.Quests
{
    /// <summary>
    /// «Разложи по конвертам» (web: NeedOrWant): an item with its price and two envelopes, «Нужно» and
    /// «Хочу». The screen decides what a pick means (the right envelope resolves the quest).
    /// </summary>
    public sealed class FinikNeedOrWantView : MonoBehaviour
    {
        [SerializeField] TMP_Text kicker;
        [SerializeField] TMP_Text item;
        [SerializeField] TMP_Text price;
        [SerializeField] TMP_Text prompt;
        [SerializeField] FinikChoiceItem needs;
        [SerializeField] FinikChoiceItem wants;

        public event Action<FinikBudgetBucket> Picked;

        public void Configure(TMP_Text kickerText, TMP_Text itemText, TMP_Text priceText, TMP_Text promptText, FinikChoiceItem needsEnvelope, FinikChoiceItem wantsEnvelope)
        {
            kicker = kickerText;
            item = itemText;
            price = priceText;
            prompt = promptText;
            needs = needsEnvelope;
            wants = wantsEnvelope;
        }

        void Awake()
        {
            if (needs) needs.Clicked += _ => Pick(FinikBudgetBucket.Needs);
            if (wants) wants.Clicked += _ => Pick(FinikBudgetBucket.Wants);
        }

        public void Show(FinikQuestMechanic settings)
        {
            if (kicker) kicker.text = settings.Kicker;
            if (item) item.text = FinikTypography.Fix(settings.Item);
            if (price) price.text = settings.Price.ToString();
            if (prompt) prompt.text = FinikTypography.Fix(settings.Prompt);
            Select(null);
            SetInteractable(true);
        }

        public void SetInteractable(bool value)
        {
            if (needs && needs.Button) needs.Button.interactable = value;
            if (wants && wants.Button) wants.Button.interactable = value;
        }

        /// <summary>Highlights the envelope the child picked last (none for null).</summary>
        public void Select(FinikBudgetBucket? bucket)
        {
            if (needs) needs.SetSelected(bucket == FinikBudgetBucket.Needs);
            if (wants) wants.SetSelected(bucket == FinikBudgetBucket.Wants);
        }

        void Pick(FinikBudgetBucket bucket)
        {
            Select(bucket);
            Picked?.Invoke(bucket);
        }
    }
}
