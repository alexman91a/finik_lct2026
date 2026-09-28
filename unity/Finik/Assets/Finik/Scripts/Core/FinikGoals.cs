using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>Something the child saves up for in the piggy bank.</summary>
    public sealed class FinikGoal
    {
        public string Id { get; }
        /// <summary>Name as a card title: «Велосипед».</summary>
        public string Title { get; }
        /// <summary>The name after «Коплю на …»: «велосипед».</summary>
        public string SavingFor { get; }
        public int Price { get; }
        /// <summary>Sprite name in Assets/Finik/UI/Art.</summary>
        public string Icon { get; }

        public FinikGoal(string id, string title, string savingFor, int price, string icon)
        {
            Id = id;
            Title = title;
            SavingFor = savingFor;
            Price = price;
            Icon = icon;
        }
    }

    /// <summary>
    /// Progress towards the current goal. The piggy bank is one pot (the wallet's savings), so switching
    /// goals keeps every coin already put aside; only the target changes.
    /// </summary>
    public readonly struct FinikGoalProgress
    {
        public readonly FinikGoal goal;
        public readonly int saved;

        public FinikGoalProgress(FinikGoal goal, int saved)
        {
            this.goal = goal;
            this.saved = Math.Max(0, saved);
        }

        public int Target => goal?.Price ?? 0;
        public int Left => Math.Max(0, Target - saved);
        public bool Reached => goal != null && saved >= Target;
        public float Fraction => Target > 0 ? Math.Min(1f, (float)saved / Target) : 0f;

        /// <summary>Progress after putting <paramref name="amount"/> more aside (negative: taking it back).</summary>
        public FinikGoalProgress After(int amount) => new(goal, saved + amount);
    }

    public enum FinikSavingsFailure
    {
        None,
        NoJourney,
        InvalidAmount,
        InsufficientFunds,
        InsufficientSavings,
        GoalReached
    }

    /// <summary>
    /// The goals on offer, cheapest first: the picker reads as a ladder, and the first card is the
    /// one a child can reach soonest. <see cref="FinikEconomyBalance"/> keeps every price inside the
    /// two-to-four-day pacing contract.
    /// </summary>
    public static class FinikGoalCatalog
    {
        public const string DefaultGoalId = "bike";

        public static readonly IReadOnlyList<FinikGoal> Goals = new[]
        {
            new FinikGoal("blocks", "Конструктор", "конструктор", 80, "goal_blocks"),
            new FinikGoal("headphones", "Наушники", "наушники", 90, "goal_headphones"),
            new FinikGoal("skateboard", "Скейтборд", "скейтборд", 100, "goal_skateboard"),
            new FinikGoal("bike", "Велосипед", "велосипед", 110, "goal_bike"),
        };

        public static bool TryGet(string id, out FinikGoal goal)
        {
            foreach (var candidate in Goals)
            {
                if (candidate.Id != id) continue;
                goal = candidate;
                return true;
            }
            goal = null;
            return false;
        }

        /// <summary>The goal with this id, or the default one for unknown or empty ids (old saves).</summary>
        public static FinikGoal Resolve(string id) =>
            TryGet(id, out var goal) || TryGet(DefaultGoalId, out goal) ? goal : Goals[0];
    }
}
