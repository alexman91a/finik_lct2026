using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Finik.UI.Onboarding
{
    /// <summary>Raises <see cref="Swiped"/> (-1 = left, +1 = right) for a quick horizontal drag.</summary>
    public sealed class FinikSwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] float minDistance = 80f;

        Vector2 start;

        /// <summary>+1 when the finger moved right (show previous), -1 when it moved left (show next).</summary>
        public event Action<int> Swiped;

        public void OnBeginDrag(PointerEventData eventData) => start = eventData.position;

        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - start;
            // Screen pixels -> canvas units so the threshold feels the same on every device.
            float scale = eventData.pressEventCamera ? 1f : GetComponentInParent<Canvas>().scaleFactor;
            if (Mathf.Abs(delta.x) / scale < minDistance || Mathf.Abs(delta.x) < Mathf.Abs(delta.y) * 1.3f) return;
            Swiped?.Invoke(delta.x > 0 ? 1 : -1);
        }
    }
}
