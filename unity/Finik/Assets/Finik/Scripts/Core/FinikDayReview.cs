using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>
    /// The score of one finished day: which of the budget habits held, and how many growth points
    /// that gave. Written once per day, either by «Завершить день» or when the next day starts.
    /// </summary>
    [Serializable]
    public sealed class FinikDayReview
    {
        public string dayKey;
        /// <summary>Coins went to «Нужно» — Finik's necessities were covered.</summary>
        public bool needs;
        /// <summary>«Нужно» and «Хочу» both stayed inside their envelopes.</summary>
        public bool plan;
        /// <summary>At least the planned amount went into the piggy bank (and something was planned).</summary>
        public bool savings;
        /// <summary>Saved on this day and on the previously reviewed day as well.</summary>
        public bool regular;
        /// <summary>Every daily task of the day was finished. A bonus, never the main source.</summary>
        public bool tasks;
        public int points;
        /// <summary>True when the child pressed «Завершить день»; false when the day was closed on rollover.</summary>
        public bool manual;
    }

    /// <summary>
    /// Pet growth driven by budget decisions over several days (п. 2.5.10): covering necessities,
    /// following the plan and saving regularly. Tasks add a small bonus. Nothing is ever taken away:
    /// a missed or messy day just adds fewer points.
    /// </summary>
    public static class FinikDayReviewEngine
    {
        public const int NeedsPoints = 1;
        public const int PlanPoints = 1;
        public const int SavingsPoints = 1;
        public const int RegularPoints = 1;
        public const int TasksPoints = 1;
        public const int MaxDayPoints = NeedsPoints + PlanPoints + SavingsPoints + RegularPoints + TasksPoints;

        /// <summary>
        /// Growth points for stage 2 and 3. A careful child gets stage 2 on day 2 and stage 3 on day 4
        /// (4 + 5 + 5 + 5); a child who keeps only part of the habits still grows, a few days later.
        /// </summary>
        public const int Stage2Points = 8;
        public const int Stage3Points = 17;

        const int KeepReviews = 30;

        public static int StageFor(int points) => points >= Stage3Points ? 3 : points >= Stage2Points ? 2 : 1;

        /// <summary>Points still missing for the next stage, or 0 at the last one.</summary>
        public static int PointsToNextStage(int points) =>
            points >= Stage3Points ? 0 : (points >= Stage2Points ? Stage3Points : Stage2Points) - points;

        public static FinikDayReview Find(FinikBudgetState state, string dayKey)
        {
            if (state?.reviews == null || string.IsNullOrEmpty(dayKey)) return null;
            foreach (var review in state.reviews) if (review != null && review.dayKey == dayKey) return review;
            return null;
        }

        public static FinikDayReview Latest(FinikBudgetState state)
        {
            if (state?.reviews == null) return null;
            for (int i = state.reviews.Count - 1; i >= 0; i--) if (state.reviews[i] != null) return state.reviews[i];
            return null;
        }

        /// <summary>
        /// Scores today's plan against today's facts. Null when there is no confirmed plan for
        /// <paramref name="dayKey"/>; the existing review when the day is already closed.
        /// </summary>
        public static FinikDayReview Close(FinikBudgetState state, string dayKey, bool manual)
        {
            if (state == null || string.IsNullOrEmpty(dayKey)) return null;
            var existing = Find(state, dayKey);
            if (existing != null) return existing;
            if (!FinikBudgetEngine.HasPlan(state, dayKey)) return null;

            var plan = state.plan;
            var actuals = state.actuals != null && state.actuals.dayKey == dayKey
                ? state.actuals
                : new FinikBudgetActuals { dayKey = dayKey };
            var previous = Latest(state);

            var review = new FinikDayReview
            {
                dayKey = dayKey,
                needs = actuals.needs > 0,
                plan = actuals.needs <= plan.needs && actuals.wants <= plan.wants,
                savings = plan.savings > 0 && actuals.savings >= plan.savings,
                tasks = state.tasksDoneDayKey == dayKey,
                manual = manual
            };
            // Skipped days do not break the habit: only the last reviewed day counts.
            review.regular = review.savings && previous != null && previous.savings;
            review.points = (review.needs ? NeedsPoints : 0) + (review.plan ? PlanPoints : 0) +
                            (review.savings ? SavingsPoints : 0) + (review.regular ? RegularPoints : 0) +
                            (review.tasks ? TasksPoints : 0);

            state.reviews ??= new List<FinikDayReview>();
            state.reviews.Add(review);
            if (state.reviews.Count > KeepReviews) state.reviews.RemoveRange(0, state.reviews.Count - KeepReviews);
            state.growthPoints += review.points;
            return review;
        }

        /// <summary>Days in a row, counting back from the latest review, on which the child saved as planned.</summary>
        public static int SavingsStreak(FinikBudgetState state)
        {
            if (state?.reviews == null) return 0;
            int streak = 0;
            for (int i = state.reviews.Count - 1; i >= 0; i--)
            {
                if (state.reviews[i] == null || !state.reviews[i].savings) break;
                streak++;
            }
            return streak;
        }

        /// <summary>What the day gave, habit by habit, for the fact card after «Завершить день».</summary>
        public static string Describe(FinikDayReview review)
        {
            if (review == null) return string.Empty;
            var lines = new List<string>
            {
                Line(review.needs, "Купил нужное для питомца", "Нужное сегодня не покупали"),
                Line(review.plan, "Уложился в конверты «Нужно» и «Хочу»", "Траты вышли за конверт"),
                Line(review.savings, "Отложил в копилку сколько задумал", "В копилку попало меньше плана")
            };
            if (review.regular) lines.Add("+ Копишь не первый день подряд");
            if (review.tasks) lines.Add("+ Все задания дня выполнены");
            lines.Add($"Очки роста за день: {review.points}");
            return string.Join("\n", lines);
        }

        static string Line(bool ok, string yes, string no) => ok ? $"+ {yes}" : $"· {no}";

        /// <summary>
        /// Why the pet is at its stage, in the child's words: what the recent days gave and what the
        /// next stage needs. Never a grade.
        /// </summary>
        public static string Reason(FinikBudgetState state, int stage, string petName)
        {
            int points = state?.growthPoints ?? 0;
            int left = PointsToNextStage(points);
            var latest = Latest(state);
            int streak = SavingsStreak(state);

            string head = latest == null
                ? $"{petName} растёт от твоих решений с деньгами. Составь бюджет, а вечером нажми «Завершить день»."
                : stage switch
                {
                    1 => $"{petName} копит силы: очков роста {points}.",
                    2 => $"{petName} подрос, потому что ты {Habits(state)}.",
                    _ => $"{petName} вырос, потому что ты {Habits(state)}."
                };

            string streakLine = streak >= 2 ? $"\n{Days(streak)} подряд ты откладывал в копилку." : string.Empty;
            string tail = left > 0
                ? $"\nДо следующей стадии: {left} {PointsWord(left)}. Очки дают нужные покупки, план и копилка."
                : string.Empty;
            return head + streakLine + tail;
        }

        /// <summary>The habits that brought most points over the reviewed days.</summary>
        static string Habits(FinikBudgetState state)
        {
            int needs = 0, plan = 0, savings = 0;
            if (state?.reviews != null)
                foreach (var r in state.reviews)
                {
                    if (r == null) continue;
                    if (r.needs) needs++;
                    if (r.plan) plan++;
                    if (r.savings) savings++;
                }
            var parts = new List<string>();
            if (needs > 0) parts.Add("покупал нужное");
            if (plan > 0) parts.Add("держался плана");
            if (savings > 0) parts.Add("откладывал в копилку");
            if (parts.Count == 0) return "составлял бюджет каждый день";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " и " + parts[parts.Count - 1];
        }

        static string Days(int n) => n % 10 == 1 && n % 100 != 11 ? $"{n} день"
            : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? $"{n} дня" : $"{n} дней";

        static string PointsWord(int n) => n % 10 == 1 && n % 100 != 11 ? "очко"
            : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? "очка" : "очков";
    }
}
