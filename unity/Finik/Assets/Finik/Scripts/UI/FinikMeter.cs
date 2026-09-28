using UnityEngine;

namespace Finik.UI
{
    /// <summary>Anything that displays a 0..1 value (bars, ring gauges).</summary>
    public abstract class FinikMeter : MonoBehaviour
    {
        public abstract float Value { get; }
        public abstract void SetValue(float value, bool animate = true);
    }
}
