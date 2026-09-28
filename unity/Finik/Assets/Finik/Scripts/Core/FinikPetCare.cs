using System;

namespace Finik.Core
{
    /// <summary>
    /// The pet's needs on a 0..100 scale, named after the HUD rings. The web app calls them
    /// energy / mood / care; Unity maps energy → Food (satiety), mood → Mood and has no care.
    /// </summary>
    [Serializable]
    public struct FinikNeeds
    {
        public float food;
        public float mood;

        public FinikNeeds(float food, float mood)
        {
            this.food = food;
            this.mood = mood;
        }

        public float Minimum => Math.Min(food, mood);
        public float Average => (food + mood) / 2f;

        public FinikNeeds Plus(FinikNeeds delta) => new(
            FinikPetCareEngine.ClampNeed(food + delta.food),
            FinikPetCareEngine.ClampNeed(mood + delta.mood));

        public override string ToString() => $"food {food:0.#} · mood {mood:0.#}";
    }

    public enum FinikCareAction
    {
        Feed = 0,
        Play = 1,
        Care = 2
    }

    [Serializable]
    public sealed class FinikPetCare
    {
        /// <summary>Needs as of <see cref="decayedAtMs"/>; read the live value via <see cref="FinikPetCareEngine.NeedsAt"/>.</summary>
        public FinikNeeds needs;
        public long decayedAtMs;
        /// <summary>Last time each <see cref="FinikCareAction"/> was performed (0 = never), indexed by the enum.</summary>
        public long[] lastActionAtMs = new long[3];
        public string lastAction;
        public long updatedAtMs;
        public long lastTapMoodAtMs;
    }

    /// <summary>
    /// Needs drift down only while the game is active. The web app has no decay; it gives feeding a reason.
    /// </summary>
    public static class FinikNeedDecay
    {
        public const float FoodPerHour = 50f;
        public const float MoodPerHour = 50f;
        public const float Floor = 0f;

        public static FinikNeeds Apply(FinikNeeds needs, double hours)
        {
            if (hours <= 0) return needs;
            return new FinikNeeds(
                Step(needs.food, FoodPerHour, hours),
                Step(needs.mood, MoodPerHour, hours));
        }

        static float Step(float value, float perHour, double hours)
        {
            if (value <= Floor) return value;
            return (float)Math.Max(Floor, value - perHour * hours);
        }
    }

    /// <summary>Port of src/domain/petCareEngine.ts (cooldowns, need deltas) plus decay.</summary>
    public static class FinikPetCareEngine
    {
        public const long CooldownMs = 15_000;
        public const float InitialNeed = 55f;
        const double MsPerHour = 3_600_000d;

        /// <summary>Same rounding as the web app's Math.round (half up), clamped to 0..100.</summary>
        public static float ClampNeed(float value) => Math.Max(0f, Math.Min(100f, (float)Math.Floor(value + 0.5f)));

        public static FinikPetCare Initial(long nowMs) => new()
        {
            needs = new FinikNeeds(InitialNeed, InitialNeed),
            decayedAtMs = nowMs,
            updatedAtMs = nowMs
        };

        public static FinikNeeds NeedsAt(FinikPetCare care, long nowMs) => care.needs;

        /// <summary>Bakes decay up to <paramref name="nowMs"/> into the stored needs.</summary>
        public static void Settle(FinikPetCare care, long nowMs)
        {
            if (nowMs <= care.decayedAtMs) return;
            care.decayedAtMs = nowMs;
        }

        public static int SecondsUntil(FinikPetCare care, FinikCareAction action, long nowMs)
        {
            long previous = LastActionAt(care, action);
            if (previous <= 0) return 0;
            long remaining = CooldownMs - (nowMs - previous);
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining / 1000d);
        }

        public static long LastActionAt(FinikPetCare care, FinikCareAction action)
        {
            int index = (int)action;
            return care.lastActionAtMs != null && index < care.lastActionAtMs.Length ? care.lastActionAtMs[index] : 0;
        }

        /// <summary>Settles decay, adds the deltas and stamps the action. Callers check the cooldown first.</summary>
        public static void Apply(FinikPetCare care, FinikCareAction action, FinikNeeds deltas, long nowMs)
        {
            Settle(care, nowMs);
            care.needs = care.needs.Plus(deltas);
            if (care.lastActionAtMs == null || care.lastActionAtMs.Length < 3)
            {
                var resized = new long[3];
                if (care.lastActionAtMs != null) Array.Copy(care.lastActionAtMs, resized, care.lastActionAtMs.Length);
                care.lastActionAtMs = resized;
            }
            care.lastActionAtMs[(int)action] = nowMs;
            care.lastAction = action.ToString().ToLowerInvariant();
            care.updatedAtMs = nowMs;
        }

        public static int Average(FinikNeeds needs) => (int)Math.Floor(needs.Average + 0.5f);
    }
}
