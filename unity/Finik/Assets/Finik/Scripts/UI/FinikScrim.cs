using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// The full-screen catcher behind a panel's card. It does two things at once: a tap beside the
    /// card does exactly what <see cref="action"/> does — close, cancel, go back — and, because it is
    /// a raycast target covering the whole screen, nothing under the open menu (the room, the HUD of
    /// a screen that does not switch it off) can be tapped while the menu is up. That is what keeps a
    /// second window from opening on top of the first.
    ///
    /// A drag that ends over the scrim is not a tap: the event system drops <c>eligibleForClick</c>
    /// as soon as a drag starts, so scrolling a list and letting go outside the card changes nothing.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class FinikScrim : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("A tap outside the card does what this button does. Usually the panel's close, cancel or back.")]
        [SerializeField] Button action;

        public Button Action
        {
            get => action;
            set => action = value;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (!action || !action.isActiveAndEnabled || !action.interactable) return;
            action.onClick.Invoke();
        }
    }
}
