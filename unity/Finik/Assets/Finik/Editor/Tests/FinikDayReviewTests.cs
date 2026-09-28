using Finik.Core;
using NUnit.Framework;

namespace Finik.Editor.Tests
{
    /// <summary>
    /// Pet growth follows budget habits over several days (п. 2.5.10), not the number of pressed
    /// buttons: necessities, the plan, the piggy bank and saving regularly.
    /// </summary>
    public sealed class FinikDayReviewTests
    {
        static FinikBudgetState Planned(string day, int needs, int wants, int savings)
        {
            var state = new FinikBudgetState();
            FinikBudgetEngine.Sync(state, day);
            state.plan = new FinikBudgetPlan { dayKey = day, income = needs + wants + savings, needs = needs, wants = wants, savings = savings };
            return state;
        }

        static void Spend(FinikBudgetState state, int needs, int wants, int savings)
        {
            FinikBudgetEngine.Add(state.actuals, FinikBudgetBucket.Needs, needs);
            FinikBudgetEngine.Add(state.actuals, FinikBudgetBucket.Wants, wants);
            FinikBudgetEngine.Add(state.actuals, FinikBudgetBucket.Savings, savings);
        }

        static void NextDay(FinikBudgetState state, string day, int needs, int wants, int savings)
        {
            FinikBudgetEngine.Sync(state, day);
            state.plan = new FinikBudgetPlan { dayKey = day, income = needs + wants + savings, needs = needs, wants = wants, savings = savings };
        }

        [Test]
        public void CarefulDayEarnsNeedsPlanAndSavingsPoints()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 8, 10, 10);
            var review = FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);

            Assert.That(review.needs && review.plan && review.savings, Is.True);
            Assert.That(review.regular, Is.False, "the first day cannot be a streak");
            Assert.That(review.points, Is.EqualTo(3));
            Assert.That(state.growthPoints, Is.EqualTo(3));
        }

        [Test]
        public void OverspentWantsAndEmptyPiggyBankEarnOnlyNeeds()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 5, 15, 0);
            var review = FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);

            Assert.That(review.needs, Is.True);
            Assert.That(review.plan, Is.False);
            Assert.That(review.savings, Is.False);
            Assert.That(review.points, Is.EqualTo(1));
        }

        [Test]
        public void TasksAreOnlyABonus()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            state.tasksDoneDayKey = "2026-09-20";
            var review = FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);

            // Nothing bought, nothing saved: the plan held, the tasks give one more point, that is all.
            Assert.That(review.points, Is.EqualTo(FinikDayReviewEngine.PlanPoints + FinikDayReviewEngine.TasksPoints));
        }

        [Test]
        public void ClosingTwiceNeverPaysTwice()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 5, 5, 10);
            var first = FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);
            var second = FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);

            Assert.That(second, Is.SameAs(first));
            Assert.That(state.growthPoints, Is.EqualTo(first.points));
            Assert.That(state.reviews.Count, Is.EqualTo(1));
        }

        [Test]
        public void NoPlanNoReview()
        {
            var state = new FinikBudgetState();
            FinikBudgetEngine.Sync(state, "2026-09-20");
            Assert.That(FinikDayReviewEngine.Close(state, "2026-09-20", manual: true), Is.Null);
            Assert.That(state.growthPoints, Is.Zero);
        }

        [Test]
        public void UnclosedDayIsScoredWhenTheNextDayStarts()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 5, 5, 10);
            FinikBudgetEngine.Sync(state, "2026-09-21");

            var review = FinikDayReviewEngine.Find(state, "2026-09-20");
            Assert.That(review, Is.Not.Null);
            Assert.That(review.manual, Is.False);
            Assert.That(state.growthPoints, Is.EqualTo(3));
        }

        [Test]
        public void SavingOnConsecutiveReviewedDaysCountsAsRegular()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 5, 5, 10);
            FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);

            // A skipped day in between does not break the habit.
            NextDay(state, "2026-09-22", 20, 10, 10);
            Spend(state, 5, 5, 10);
            var review = FinikDayReviewEngine.Close(state, "2026-09-22", manual: true);

            Assert.That(review.regular, Is.True);
            Assert.That(FinikDayReviewEngine.SavingsStreak(state), Is.EqualTo(2));
        }

        [Test]
        public void CarefulChildGrowsOnDayTwoAndDayFour()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            string[] days = { "2026-09-20", "2026-09-21", "2026-09-22", "2026-09-23" };
            int[] expectedStage = { 1, 2, 2, 3 };
            for (int i = 0; i < days.Length; i++)
            {
                if (i > 0) NextDay(state, days[i], 20, 10, 10);
                Spend(state, 5, 5, 10);
                state.tasksDoneDayKey = days[i];
                FinikDayReviewEngine.Close(state, days[i], manual: true);
                Assert.That(FinikDayReviewEngine.StageFor(state.growthPoints), Is.EqualTo(expectedStage[i]), days[i]);
            }
        }

        [Test]
        public void StageThresholdsAndPointsLeft()
        {
            Assert.That(FinikDayReviewEngine.StageFor(0), Is.EqualTo(1));
            Assert.That(FinikDayReviewEngine.StageFor(FinikDayReviewEngine.Stage2Points - 1), Is.EqualTo(1));
            Assert.That(FinikDayReviewEngine.StageFor(FinikDayReviewEngine.Stage2Points), Is.EqualTo(2));
            Assert.That(FinikDayReviewEngine.StageFor(FinikDayReviewEngine.Stage3Points), Is.EqualTo(3));
            Assert.That(FinikDayReviewEngine.PointsToNextStage(3), Is.EqualTo(FinikDayReviewEngine.Stage2Points - 3));
            Assert.That(FinikDayReviewEngine.PointsToNextStage(FinikDayReviewEngine.Stage3Points), Is.Zero);
        }

        [Test]
        public void ReasonNamesTheHabitsNotTheButtons()
        {
            var state = Planned("2026-09-20", 20, 10, 10);
            Spend(state, 5, 5, 10);
            FinikDayReviewEngine.Close(state, "2026-09-20", manual: true);
            string reason = FinikDayReviewEngine.Reason(state, 2, "Финик");

            Assert.That(reason, Does.Contain("откладывал в копилку"));
            Assert.That(reason, Does.Not.Contain("задания"));
        }
    }
}
