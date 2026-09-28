using UnityEngine;

namespace Finik.UI
{
    /// <summary>Small easing / spring helpers shared by the HUD widgets.</summary>
    public static class FinikUiMotion
    {
        public static float EaseOutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        public static float EaseOutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        /// <summary>Semi-implicit damped spring step. Stable for the frame times UI sees.</summary>
        public static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            dt = Mathf.Min(dt, 1f / 30f);
            velocity += (target - value) * stiffness * dt;
            velocity *= Mathf.Exp(-damping * dt);
            value += velocity * dt;
        }
    }
}
