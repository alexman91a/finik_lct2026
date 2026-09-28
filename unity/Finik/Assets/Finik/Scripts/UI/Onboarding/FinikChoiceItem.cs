using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Onboarding
{
    /// <summary>
    /// A selectable card or chip (pet, name idea, accessory). The owner decides what "selected" means;
    /// this only renders the state: highlight ring fades in, the card rises slightly, a check pops.
    /// </summary>
    public sealed class FinikChoiceItem : MonoBehaviour
    {
        [SerializeField] string id;
        [SerializeField] Button button;
        [SerializeField] FinikPressFeedback press;
        [SerializeField] Graphic highlight;
        [SerializeField] GameObject check;
        [SerializeField] TMP_Text label;
        [SerializeField] Color labelColor = new(0.12f, 0.15f, 0.36f, 1f);
        [SerializeField] Color selectedLabelColor = Color.white;
        [SerializeField, Range(1f, 1.2f)] float selectedScale = 1.06f;

        bool selected;
        float amount;

        public string Id => id;
        public Button Button => button;
        public bool Selected => selected;

        public event Action<FinikChoiceItem> Clicked;

        public void Configure(string itemId, Button itemButton, FinikPressFeedback feedback, Graphic ring, GameObject checkMark, TMP_Text caption)
        {
            id = itemId;
            button = itemButton;
            press = feedback;
            highlight = ring;
            check = checkMark;
            label = caption;
        }

        bool hooked;

        void Awake() => Hook();

        /// <summary>
        /// Also on enable, and guarded so it only ever happens once. The editor runs with domain and
        /// scene reload disabled: a component already in the scene can reach Play without its Awake,
        /// and a script recompile during Play wipes the listener while leaving the object standing.
        /// Either way the card stops responding to taps, which is the whole of what it does.
        /// </summary>
        void OnEnable() => Hook();

        void Hook()
        {
            if (hooked) return;
            hooked = true;
            if (button) button.onClick.AddListener(() => Clicked?.Invoke(this));
            if (label) labelColor = label.color;
            Render();
        }

        public void SetSelected(bool value)
        {
            if (value && !selected && press) press.Punch(0.8f);
            selected = value;
            if (press) press.BaseScale = value ? selectedScale : 1f;
            if (check) check.SetActive(value);
        }

        /// <summary>Shows a completion check without making the card look like the active choice.</summary>
        public void SetCompleted(bool value)
        {
            if (check) check.SetActive(value);
        }

        public void SetSelectedScale(float scale)
        {
            selectedScale = Mathf.Clamp(scale, 1f, 1.2f);
            if (press) press.BaseScale = selected ? selectedScale : 1f;
        }

        void Update()
        {
            float goal = selected ? 1f : 0f;
            if (Mathf.Approximately(amount, goal)) return;
            amount = Mathf.MoveTowards(amount, goal, Time.unscaledDeltaTime * 6f);
            Render();
        }

        void Render()
        {
            if (highlight)
            {
                var c = highlight.color;
                c.a = amount;
                highlight.color = c;
                highlight.enabled = amount > 0.001f;
            }
            if (label) label.color = Color.Lerp(labelColor, selectedLabelColor, amount);
        }
    }
}
