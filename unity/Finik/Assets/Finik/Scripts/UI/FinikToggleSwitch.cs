using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// An on/off pill: the knob slides to the right and the track fills green when it is on. The word
    /// under the knob («Вкл» / «Выкл») says the same thing without relying on colour alone.
    ///
    /// The owner decides what the state means; a tap only raises <see cref="Toggled"/> with the value
    /// it asks for, and the owner answers with <see cref="SetIsOn"/>.
    /// </summary>
    public sealed class FinikToggleSwitch : MonoBehaviour
    {
        [SerializeField] Button button;
        [Tooltip("The green fill that fades in over the track when the switch is on.")]
        [SerializeField] Graphic onFill;
        [SerializeField] RectTransform knob;
        [SerializeField] TMP_Text stateLabel;
        [SerializeField] string onText = "Вкл";
        [SerializeField] string offText = "Выкл";
        [SerializeField] Color onLabelColor = Color.white;
        [SerializeField] Color offLabelColor = new(0.36f, 0.41f, 0.62f, 1f);
        [SerializeField, Min(0.01f)] float slideSeconds = 0.14f;
        [Tooltip("How far above the middle the green pill's face sits, as a share of the height: the candy has a lip along its bottom edge, so its visual centre is higher than the rect's.")]
        [SerializeField, Range(0f, 0.2f)] float onFaceLift = 0.06f;

        bool isOn;
        float amount;
        bool hooked;

        public bool IsOn => isOn;
        public Button Button => button;

        /// <summary>The value the tap asks for: the opposite of the current one.</summary>
        public event Action<bool> Toggled;

        void Awake() => Hook();

        // Domain reload is off in the editor: a switch can reach Play without Awake, see FinikChoiceItem.
        void OnEnable()
        {
            Hook();
            Render();
        }

        void Hook()
        {
            if (hooked) return;
            hooked = true;
            if (button) button.onClick.AddListener(() => Toggled?.Invoke(!isOn));
        }

        public void SetIsOn(bool value, bool animate = true)
        {
            isOn = value;
            if (!animate || !isActiveAndEnabled) amount = value ? 1f : 0f;
            Render();
        }

        public void SetInteractable(bool value)
        {
            if (button) button.interactable = value;
            var group = GetComponent<CanvasGroup>();
            if (!group) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = value ? 1f : 0.5f;
        }

        void Update()
        {
            float goal = isOn ? 1f : 0f;
            if (Mathf.Approximately(amount, goal)) return;
            amount = Mathf.MoveTowards(amount, goal, Time.unscaledDeltaTime / slideSeconds);
            Render();
        }

        void Render()
        {
            float eased = amount * amount * (3f - 2f * amount);
            if (onFill)
            {
                var c = onFill.color;
                c.a = eased;
                onFill.color = c;
                onFill.enabled = eased > 0.001f;
            }
            if (!knob) return;
            // The knob rides between the two ends of its parent, inset by its own margin, and rises
            // onto the green face as the switch turns on.
            var parent = (RectTransform)knob.parent;
            float width = parent.rect.width, height = parent.rect.height;
            float knobWidth = knob.rect.width;
            float inset = (height - knob.rect.height) * 0.5f;
            float travel = Mathf.Max(0f, width - knobWidth - 2f * inset);
            float lift = height * onFaceLift;
            knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.anchoredPosition = new Vector2(inset + knobWidth * 0.5f + travel * eased, lift * eased);

            if (!stateLabel) return;
            stateLabel.text = isOn ? onText : offText;
            stateLabel.color = isOn ? onLabelColor : offLabelColor;
            // The word is centred in the part of the track the knob does not cover — left of it when
            // on, right of it when off — and on the green face's own middle, not the rect's.
            float freeStart = isOn ? inset : inset + knobWidth;
            float freeEnd = isOn ? width - inset - knobWidth : width - inset;
            var rect = stateLabel.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, freeEnd - freeStart), 0f);
            rect.anchoredPosition = new Vector2((freeStart + freeEnd) * 0.5f, isOn ? lift : 0f);
        }

        void OnRectTransformDimensionsChange()
        {
            if (knob) Render();
        }
    }
}
