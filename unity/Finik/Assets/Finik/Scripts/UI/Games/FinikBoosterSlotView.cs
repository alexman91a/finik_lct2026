using System;
using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// One super power in the row under the board: its picture, how many charges are left, and a ring
    /// while it is armed and waiting for the player to pick a piece. A spent power stays in place,
    /// greyed out, so the row never jumps around mid-level.
    /// </summary>
    public sealed class FinikBoosterSlotView : MonoBehaviour
    {
        [SerializeField] string boosterId;
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text count;
        [SerializeField] GameObject badge;
        [Tooltip("Ring that lights up while this power waits for a target.")]
        [SerializeField] Image armedRing;
        [SerializeField] FinikIdleMotion idle;
        [Tooltip("The power's name under its slot: a picture alone left the player guessing what it does.")]
        [SerializeField] TMP_Text title;

        public event Action<string> Clicked;

        public string BoosterId => boosterId;

        public void Configure(Button slotButton, Image slotIcon, TMP_Text slotCount, GameObject countBadge, Image ring, FinikIdleMotion motion,
            TMP_Text slotTitle)
        {
            title = slotTitle;
            button = slotButton;
            icon = slotIcon;
            count = slotCount;
            badge = countBadge;
            armedRing = ring;
            idle = motion;
        }

        void Awake()
        {
            if (!button) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Clicked?.Invoke(boosterId));
        }

        public void Show(FinikBooster booster, Sprite sprite, int charges, bool armed)
        {
            bool used = booster != null;
            gameObject.SetActive(used);
            if (!used) return;

            boosterId = booster.Id;
            if (icon)
            {
                if (sprite) icon.sprite = sprite;
                // A spent power keeps its picture but goes quiet, so the row stays readable.
                icon.color = charges > 0 ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            }
            if (count) count.text = charges.ToString();
            if (title)
            {
                title.text = booster.Title;
                title.alpha = charges > 0 ? 1f : 0.45f;
            }
            if (badge) badge.SetActive(charges > 0);
            if (button) button.interactable = charges > 0;
            if (armedRing) armedRing.gameObject.SetActive(armed);
            // Only an armed power bobs; three of them dancing at once is noise.
            if (idle) idle.enabled = armed;
        }
    }
}
