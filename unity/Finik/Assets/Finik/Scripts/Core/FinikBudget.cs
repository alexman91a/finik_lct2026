using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>The plan for one day: how many coins go to «Нужно», «Хочу» and «Коплю».</summary>
    [Serializable]
    public sealed class FinikBudgetPlan
    {
        /// <summary>Day the plan belongs to, as <see cref="FinikQuestEngine.DayKey"/>.</summary>
        public string dayKey;
        /// <summary>Coins the plan was drawn up for: the wallet at the moment it was confirmed.</summary>
        public int income;
        public int needs;
        public int wants;
        public int savings;
        public long confirmedAtMs;

        public int Total => needs + wants + savings;
    }

    /// <summary>What actually happened that day, filled in by purchases, food and savings transfers.</summary>
    [Serializable]
    public sealed class FinikBudgetActuals
    {
        public string dayKey;
        public int needs;
        public int wants;
        public int savings;

        public int Total => needs + wants + savings;
    }

    /// <summary>Persisted budget state: today's plan/facts plus the most recently finished day.</summary>
    [Serializable]
    public sealed class FinikBudgetState
    {
        public FinikBudgetPlan plan = new();
        public FinikBudgetActuals actuals = new();
        public FinikBudgetPlan lastPlan = new();
        public FinikBudgetActuals lastActuals = new();
        /// <summary>Scored days, oldest first; see <see cref="FinikDayReviewEngine"/>.</summary>
        public List<FinikDayReview> reviews = new();
        /// <summary>Growth points earned by all reviewed days. Only ever grows.</summary>
        public int growthPoints;
        /// <summary>Budget day on which every daily task was finished — the review's small bonus.</summary>
        public string tasksDoneDayKey;
    }

    public enum FinikBudgetFailure
    {
        None,
        NoJourney,
        AlreadyConfirmed,
        NotAllCoinsPlanned,
        NoCoins
    }

    /// <summary>One row of the plan/fact comparison the child and the adult section both read.</summary>
    public readonly struct FinikBudgetRow
    {
        public readonly FinikBudgetBucket bucket;
        public readonly int planned;
        public readonly int actual;

        public FinikBudgetRow(FinikBudgetBucket bucket, int planned, int actual)
        {
            this.bucket = bucket;
            this.planned = planned;
            this.actual = actual;
        }

        public int Variance => actual - planned;
        public string Label => FinikFoodCatalog.BucketLabel(bucket);

        /// <summary>
        /// Whether this row followed the plan. Spending buckets must stay inside their envelope;
        /// «Коплю» is the opposite — putting aside more than planned is following the plan, not
        /// breaking it.
        /// </summary>
        public bool OnPlan => bucket == FinikBudgetBucket.Savings ? actual >= planned : actual <= planned;
    }

    /// <summary>The whole plan/fact picture of one day.</summary>
    public readonly struct FinikBudgetSummary
    {
        public readonly bool hasPlan;
        public readonly int income;
        public readonly IReadOnlyList<FinikBudgetRow> rows;

        public FinikBudgetSummary(bool hasPlan, int income, IReadOnlyList<FinikBudgetRow> rows)
        {
            this.hasPlan = hasPlan;
            this.income = income;
            this.rows = rows ?? Array.Empty<FinikBudgetRow>();
        }

        public int Planned
        {
            get { int sum = 0; foreach (var row in rows) sum += row.planned; return sum; }
        }

        public int Actual
        {
            get { int sum = 0; foreach (var row in rows) sum += row.actual; return sum; }
        }

        public bool OnPlan
        {
            get { foreach (var row in rows) if (!row.OnPlan) return false; return rows.Count > 0; }
        }

        /// <summary>Neutral sentence about the day. Never a grade, never a comparison with anyone.</summary>
        public string Conclusion => !hasPlan
            ? "План на этот день не составлен, поэтому сравнивать пока нечего."
            : OnPlan
                ? "Удалось придерживаться плана."
                : "План и решения немного разошлись — теперь видно, где именно.";
    }

    /// <summary>
    /// The plan half of the day cycle: получил монеты → <b>составил бюджет</b> → выполнил квест →
    /// получил монеты → совершил покупки → отложил в копилку → увидел план и факт.
    ///
    /// The plan never moves coins. The wallet keeps every coin until a real operation happens, and each
    /// operation is recorded against its bucket here, so the wallet, the plan and the day's summary can
    /// never drift apart.
    /// </summary>
    public static class FinikBudgetEngine
    {
        public static readonly FinikBudgetBucket[] Buckets =
        {
            FinikBudgetBucket.Needs,
            FinikBudgetBucket.Wants,
            FinikBudgetBucket.Savings
        };

        /// <summary>
        /// Coins one −/+ tap moves, and the grain the suggestion is drawn on. Small enough to think
        /// in, large enough not to be tedious.
        /// </summary>
        public const int Step = 5;

        /// <summary>
        /// Suggested split of <paramref name="income"/>: half on «Нужно», the rest shared.
        ///
        /// The whole suggestion is laid out in <see cref="Step"/>-coin pieces, so every envelope shows
        /// a round number the −/+ controls can actually reproduce. Coins that do not make up a whole
        /// piece (the wallet is not always a multiple of <see cref="Step"/>) join «Нужно»: necessities
        /// come first, and the plan must still add up to the last coin.
        /// </summary>
        public static FinikBudgetPlan Suggest(string dayKey, int income)
        {
            int safe = Math.Max(0, income);
            int pieces = safe / Step;
            int stray = safe - pieces * Step;
            int needsPieces = pieces / 2;
            int savingsPieces = (pieces - needsPieces) / 2;
            int needs = needsPieces * Step + stray;
            int savings = savingsPieces * Step;
            return new FinikBudgetPlan
            {
                dayKey = dayKey,
                income = safe,
                needs = needs,
                wants = safe - needs - savings,
                savings = savings
            };
        }

        public static bool HasPlan(FinikBudgetState state, string dayKey) =>
            state?.plan != null && state.plan.dayKey == dayKey && state.plan.income > 0;

        public static int Get(FinikBudgetPlan plan, FinikBudgetBucket bucket) => bucket switch
        {
            FinikBudgetBucket.Needs => plan.needs,
            FinikBudgetBucket.Wants => plan.wants,
            FinikBudgetBucket.Savings => plan.savings,
            _ => 0
        };

        public static int Get(FinikBudgetActuals actuals, FinikBudgetBucket bucket) => bucket switch
        {
            FinikBudgetBucket.Needs => actuals.needs,
            FinikBudgetBucket.Wants => actuals.wants,
            FinikBudgetBucket.Savings => actuals.savings,
            _ => 0
        };

        public static void Add(FinikBudgetActuals actuals, FinikBudgetBucket bucket, int amount)
        {
            if (amount <= 0) return;
            switch (bucket)
            {
                case FinikBudgetBucket.Needs: actuals.needs += amount; break;
                case FinikBudgetBucket.Wants: actuals.wants += amount; break;
                case FinikBudgetBucket.Savings: actuals.savings += amount; break;
            }
        }

        /// <summary>Coins of the confirmed plan this bucket has not spent yet.</summary>
        public static int Left(FinikBudgetState state, string dayKey, FinikBudgetBucket bucket)
        {
            if (!HasPlan(state, dayKey)) return 0;
            int spent = state.actuals != null && state.actuals.dayKey == dayKey ? Get(state.actuals, bucket) : 0;
            return Math.Max(0, Get(state.plan, bucket) - spent);
        }

        /// <summary>Moves the plan and the facts to <paramref name="dayKey"/>. True when anything changed.</summary>
        public static bool Sync(FinikBudgetState state, string dayKey)
        {
            if (state == null) return false;
            bool changed = false;
            if (state.plan == null) { state.plan = new FinikBudgetPlan(); changed = true; }
            if (state.actuals == null) { state.actuals = new FinikBudgetActuals(); changed = true; }
            // A new day starts without a plan, but keep one finished snapshot for
            // «Мой прогресс». Otherwise yesterday's plan/fact disappears before the child can review it.
            if (state.plan.dayKey != dayKey && !string.IsNullOrEmpty(state.plan.dayKey))
            {
                // A day the child never closed is still scored, so leaving without the button costs nothing.
                FinikDayReviewEngine.Close(state, state.plan.dayKey, manual: false);
                state.lastPlan = Copy(state.plan);
                state.lastActuals = state.actuals != null && state.actuals.dayKey == state.plan.dayKey
                    ? Copy(state.actuals)
                    : new FinikBudgetActuals { dayKey = state.plan.dayKey };
                state.plan = new FinikBudgetPlan();
                changed = true;
            }
            if (state.actuals.dayKey != dayKey)
            {
                state.actuals = new FinikBudgetActuals { dayKey = dayKey };
                changed = true;
            }
            return changed;
        }

        public static FinikBudgetSummary Summarize(FinikBudgetState state, string dayKey) =>
            Summarize(state?.plan, state?.actuals, dayKey);

        public static FinikBudgetSummary LatestSummary(FinikBudgetState state, string currentDayKey)
        {
            if (HasPlan(state, currentDayKey)) return Summarize(state.plan, state.actuals, currentDayKey);
            string lastDay = state?.lastPlan?.dayKey;
            return string.IsNullOrEmpty(lastDay)
                ? new FinikBudgetSummary(false, 0, Array.Empty<FinikBudgetRow>())
                : Summarize(state.lastPlan, state.lastActuals, lastDay);
        }

        public static string LatestDayKey(FinikBudgetState state, string currentDayKey) =>
            HasPlan(state, currentDayKey) ? currentDayKey : state?.lastPlan?.dayKey ?? string.Empty;

        static FinikBudgetSummary Summarize(FinikBudgetPlan plan, FinikBudgetActuals actuals, string dayKey)
        {
            bool hasPlan = plan != null && plan.dayKey == dayKey && plan.income > 0;
            var rows = new List<FinikBudgetRow>(Buckets.Length);
            foreach (var bucket in Buckets)
            {
                int planned = hasPlan ? Get(plan, bucket) : 0;
                int actual = actuals != null && actuals.dayKey == dayKey ? Get(actuals, bucket) : 0;
                rows.Add(new FinikBudgetRow(bucket, planned, actual));
            }
            return new FinikBudgetSummary(hasPlan, hasPlan ? plan.income : 0, rows);
        }

        static FinikBudgetPlan Copy(FinikBudgetPlan source) => source == null ? new FinikBudgetPlan() : new FinikBudgetPlan
        {
            dayKey = source.dayKey, income = source.income, needs = source.needs,
            wants = source.wants, savings = source.savings, confirmedAtMs = source.confirmedAtMs
        };

        static FinikBudgetActuals Copy(FinikBudgetActuals source) => source == null ? new FinikBudgetActuals() : new FinikBudgetActuals
        {
            dayKey = source.dayKey, needs = source.needs, wants = source.wants, savings = source.savings
        };

        /// <summary>Which envelope a shop item is charged to.</summary>
        public static FinikBudgetBucket BucketFor(FinikShopCategory category) =>
            category == FinikShopCategory.Need ? FinikBudgetBucket.Needs : FinikBudgetBucket.Wants;
    }
}
