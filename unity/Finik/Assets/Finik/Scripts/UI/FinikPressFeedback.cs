using UnityEngine;
using UnityEngine.EventSystems;

namespace Finik.UI
{
    /// <summary>
    /// Springy squash on press and a small bounce on release. Drives <see cref="target"/>'s scale only,
    /// so it never fights layout groups or anchored positions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FinikPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        [SerializeField] Transform target;
        [SerializeField, Range(0.7f, 1f)] float pressedScale = 0.9f;
        [SerializeField, Min(0f)] float releaseKick = 2.4f;
        [SerializeField] float stiffness = 520f;
        [SerializeField] float damping = 16f;

        float baseScale = 1f;
        float scale = 1f;
        float velocity;
        bool held;
        bool pointerInside;

        public Transform Target => target ? target : transform;

        /// <summary>Resting scale, e.g. raised for a selected tab.</summary>
        public float BaseScale
        {
            get => baseScale;
            set => baseScale = value;
        }

        void OnEnable()
        {
            scale = baseScale;
            velocity = 0f;
            held = false;
            Target.localScale = Vector3.one * scale;
        }

        void Update()
        {
            float goal = held && pointerInside ? baseScale * pressedScale : baseScale;
            FinikUiMotion.Spring(ref scale, ref velocity, goal, stiffness, damping, Time.unscaledDeltaTime);
            Target.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Plays a bounce without user input, e.g. when a value changes.</summary>
        public void Punch(float strength = 1f) => velocity += releaseKick * strength;

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            pointerInside = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (held && pointerInside) velocity += releaseKick;
            held = false;
        }

        public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;

        public void OnPointerExit(PointerEventData eventData) => pointerInside = false;
    }
}
