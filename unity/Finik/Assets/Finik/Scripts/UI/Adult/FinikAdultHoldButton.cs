using System;
using Finik.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Finik.UI.Adult
{
    /// <summary>
    /// A button that only fires after being held down. Releasing early resets the fill, so a child
    /// tapping around cannot open the adult section by accident.
    /// </summary>
    public sealed class FinikAdultHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDragHandler
    {
        [SerializeField] RectTransform fill;
        [SerializeField, Min(0.2f)] float seconds = FinikAdult.HoldSeconds;
        [Tooltip("Finger may drift this many screen pixels while holding before the gesture is cancelled.")]
        [SerializeField, Min(16f)] float movementTolerance = 96f;

        float heldFor;
        bool holding;
        bool fired;
        int activePointerId = int.MinValue;
        Vector2 pressPosition;

        public event Action Held;

        public void Configure(RectTransform progressFill)
        {
            fill = progressFill;
            ResetProgress();
        }

        public void ResetProgress()
        {
            holding = false;
            fired = false;
            heldFor = 0f;
            activePointerId = int.MinValue;
            Apply(0f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            holding = true;
            fired = false;
            heldFor = 0f;
            activePointerId = eventData.pointerId;
            pressPosition = eventData.position;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (activePointerId == int.MinValue || eventData.pointerId == activePointerId)
                ResetProgress();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            CancelIfMovedTooFar(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            CancelIfMovedTooFar(eventData);
        }

        void CancelIfMovedTooFar(PointerEventData eventData)
        {
            if (!holding || eventData.pointerId != activePointerId) return;
            if (Vector2.Distance(pressPosition, eventData.position) <= movementTolerance) return;
            ResetProgress();
        }

        void Update()
        {
            if (!holding || fired) return;
            heldFor += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(heldFor / Mathf.Max(0.2f, seconds));
            Apply(progress);
            if (progress < 1f) return;
            fired = true;
            holding = false;
            Apply(0f);
            Held?.Invoke();
        }

        void Apply(float progress)
        {
            if (!fill) return;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(progress, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }
    }
}
