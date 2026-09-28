using Finik.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Finik.UI
{
    /// <summary>Lets a UI press take priority over a nonessential pet gesture.</summary>
    [DisallowMultipleComponent]
    public sealed class FinikUiActivityInterrupt : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            var activity = FindFirstObjectByType<FinikActivityController>();
            if (activity) activity.InterruptForUserInput();
        }
    }
}
