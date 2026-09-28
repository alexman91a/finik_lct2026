using Tayx.Graphy;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>
    /// The on-screen frame counter (Graphy, com.tayx.graphy), switched from «Для взрослых».
    ///
    /// The overlay lives in the scene as a deactivated object built by <c>Finik/UI/Rebuild Frame
    /// Counter</c>, so that a build without it still runs: everything here is a no-op when the object
    /// is missing. While the counter is off the whole object is deactivated rather than merely
    /// hidden — a monitor nobody asked for should cost nothing.
    /// </summary>
    public static class FinikGraphy
    {
        static GraphyManager cached;

        /// <summary>The overlay in the current scene, or null when it was never built into it.</summary>
        static GraphyManager Manager
        {
            get
            {
                if (cached) return cached;
                cached = Object.FindFirstObjectByType<GraphyManager>(FindObjectsInactive.Include);
                return cached;
            }
        }

        /// <summary>Whether the scene actually carries the overlay.</summary>
        public static bool Available => Manager;

        public static void SetVisible(bool visible)
        {
            var manager = Manager;
            if (!manager) return;

            manager.gameObject.SetActive(visible);
            // Graphy initialises in Start, so this lands either way: before Init it only records the
            // wanted state, after Init it applies it straight away.
            if (visible) manager.Enable();
            else manager.Disable();
        }

        /// <summary>Drops the cached lookup. Statics survive Play Mode with domain reload disabled.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cached = null;
    }
}
