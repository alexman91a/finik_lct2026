using System;
using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;

namespace Finik.UI.Quests
{
    /// <summary>One answer of a plain quest (web: ChoiceTile): a numbered card with the choice text.</summary>
    public sealed class FinikQuestOptionView : MonoBehaviour
    {
        [SerializeField] FinikChoiceItem choice;
        [SerializeField] TMP_Text number;
        [SerializeField] TMP_Text label;

        public string ChoiceId { get; private set; }
        public FinikChoiceItem Choice => choice;
        public event Action<FinikQuestOptionView> Picked;

        public void Configure(FinikChoiceItem item, TMP_Text numberText, TMP_Text labelText)
        {
            choice = item;
            number = numberText;
            label = labelText;
        }

        void Awake()
        {
            if (choice) choice.Clicked += _ => Picked?.Invoke(this);
        }

        public void Show(FinikQuestChoice option, int index)
        {
            ChoiceId = option.Id;
            if (number) number.text = (index + 1).ToString();
            if (label) label.text = FinikTypography.Fix(FinikQuestText.Resolve(option.Label));
            if (choice)
            {
                choice.SetSelected(false);

                // Answer appearance must not hint whether a choice is good or bad.
                // Older scene data used green/orange tints by slot, so normalise it at runtime too.
                if (choice.Button && choice.Button.targetGraphic)
                    choice.Button.targetGraphic.color = new Color(0.949f, 0.925f, 1f, 1f); // #F2ECFF
                var badge = choice.transform.Find("Body/Badge")?.GetComponent<UnityEngine.UI.Image>();
                if (badge) badge.color = new Color(0.55f, 0.3f, 0.95f, 1f);
                var sparkle = choice.transform.Find("Body/Sparkle");
                if (sparkle) sparkle.gameObject.SetActive(false);
            }
            SetInteractable(true);
        }

        public void SetInteractable(bool value)
        {
            if (choice && choice.Button) choice.Button.interactable = value;
        }
    }
}
