using System;
using System.Linq;
using Finik.Core;
using Finik.UI.Quests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Editor.Tests
{
    public sealed class FinikQuestTests
    {
        static readonly DateTime DayOne = new(2026, 9, 20, 10, 0, 0);

        [Test]
        public void SingleResultActionReleasesTheHiddenButtonsLayoutSpace()
        {
            var host = new GameObject("ResultTest");
            try
            {
                var view = host.AddComponent<FinikQuestResultView>();
                var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(host.transform, false);
                var closeRoot = new GameObject("Close", typeof(RectTransform));
                closeRoot.transform.SetParent(row.transform, false);
                var closeBody = new GameObject("Body", typeof(RectTransform), typeof(Button));
                closeBody.transform.SetParent(closeRoot.transform, false);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(FinikQuestResultView).GetField("closeButton", flags).SetValue(view, closeBody.GetComponent<Button>());

                view.SetRoomExitVisible(false);
                Assert.That(closeRoot.activeSelf, Is.False);
                view.SetRoomExitVisible(true);
                Assert.That(closeRoot.activeSelf, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void DailyQuestsUnlockInOrderAndShopWaitsForAllAnswers()
        {
            var state = new FinikQuestState();
            var tasks = FinikQuestEngine.BuildTasks(DayOne, 1);
            FinikQuestEngine.Sync(state, tasks, DayOne, 1);
            var daily = tasks.Where(t => !t.IsWeekly).ToArray();

            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[0].Id), Is.True);
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[1].Id), Is.False);
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[2].Id), Is.False);

            state.Find(daily[0].Id).status = FinikQuestStatus.Active;
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[1].Id), Is.False);

            state.Find(daily[0].Id).status = FinikQuestStatus.Completed;
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[1].Id), Is.True);
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[2].Id), Is.False);

            state.Find(daily[1].Id).status = FinikQuestStatus.Claimed;
            Assert.That(FinikQuestEngine.IsUnlocked(state, tasks, daily[2].Id), Is.True);
            Assert.That(FinikQuestEngine.AllDailyAnswered(state, tasks), Is.False);

            state.Find(daily[2].Id).status = FinikQuestStatus.Completed;
            Assert.That(FinikQuestEngine.AllDailyAnswered(state, tasks), Is.True);

            var tomorrow = FinikQuestEngine.BuildTasks(DayOne.AddDays(1), 2);
            FinikQuestEngine.Sync(state, tomorrow, DayOne.AddDays(1), 2);
            Assert.That(FinikQuestEngine.AllDailyAnswered(state, tomorrow), Is.False);
            Assert.That(FinikQuestEngine.IsUnlocked(state, tomorrow, tomorrow[1].Id), Is.False);
        }

        [Test]
        public void ShippedCatalogHasThreeScenariosPerDayAndExpectedQuestionCounts()
        {
            var asset = Resources.Load<TextAsset>(FinikQuestCatalog.ResourcePath);
            Assert.That(asset, Is.Not.Null);
            var problems = FinikQuestCatalog.Parse(asset.text, out var content);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(content.quests.Length, Is.EqualTo(15));
            Assert.That(content.dailyCount, Is.EqualTo(3));

            for (int day = 1; day <= 5; day++)
            {
                var quests = content.quests.Where(q => q.Day == day).OrderBy(q => q.Order).ToArray();
                Assert.That(quests.Length, Is.EqualTo(3), $"day {day}");
                int expectedQuestions = day == 2 || day == 5 ? 9 : 7;
                Assert.That(quests.Sum(q => q.Questions.Length), Is.EqualTo(expectedQuestions), $"day {day}");
                Assert.That(quests.Select(q => q.Order), Is.EqualTo(new[] { 1, 2, 3 }));
            }
        }

        [Test]
        public void CampaignBoardShowsThreeScenariosAndOnlyFirstQuestHasNoReward()
        {
            for (int day = 1; day <= 5; day++)
            {
                var tasks = FinikQuestEngine.BuildTasks(DayOne.AddDays(day - 1), day);
                var daily = tasks.Where(t => !t.IsWeekly).ToArray();
                Assert.That(daily.Length, Is.EqualTo(3));
                Assert.That(daily.Select(t => t.Reward), Is.EqualTo(day == 1
                    ? new[] { 0, 10, 10 }
                    : new[] { 10, 10, 10 }), $"day {day}");
                int expectedQuestions = day == 2 || day == 5 ? 9 : 7;
                Assert.That(daily.Sum(t => t.Quest.Questions.Length), Is.EqualTo(expectedQuestions));

                // The streak closes the board and is the only task without a scenario behind it.
                var streak = tasks.SingleOrDefault(t => t.IsWeekly);
                Assert.That(streak, Is.Not.Null, $"day {day}: the weekly streak is missing from the board");
                Assert.That(streak.Quest, Is.Null);
                Assert.That(streak.Reward, Is.Zero);
                Assert.That(FinikQuestCatalog.Weekly.Reward, Is.Zero);
                Assert.That(streak.TargetCount, Is.EqualTo(FinikQuestCatalog.Weekly.Target));
                Assert.That(tasks[^1], Is.SameAs(streak));
            }
        }

        [Test]
        public void TheStreakKeepsOneIdPerWeekSoItsProgressSurvivesANewDay()
        {
            var monday = new DateTime(2026, 9, 21);
            var tuesday = monday.AddDays(1);
            var nextMonday = monday.AddDays(7);

            string Streak(DateTime day) => FinikQuestEngine.BuildTasks(day, 1).Single(t => t.IsWeekly).Id;

            Assert.That(Streak(tuesday), Is.EqualTo(Streak(monday)));
            Assert.That(Streak(nextMonday), Is.Not.EqualTo(Streak(monday)));
        }

        [Test]
        public void ClaimingThreeDailyQuestsUnlocksTheStreak()
        {
            var state = new FinikQuestState { campaignStartDayKey = "2026-09-20" };
            var tasks = FinikQuestEngine.BuildTasks(DayOne, 1);
            FinikQuestEngine.Sync(state, tasks, DayOne, 1);

            var streak = tasks.Single(t => t.IsWeekly);
            Assert.That(state.Find(streak.Id).status, Is.EqualTo(FinikQuestStatus.Available));

            // Sync promotes the streak as soon as the week's claims reach its target.
            state.weeklyClaimedDaily = streak.TargetCount;
            state.dayKey = null;
            FinikQuestEngine.Sync(state, tasks, DayOne, 1);
            Assert.That(state.Find(streak.Id).status, Is.EqualTo(FinikQuestStatus.Claimed));
        }

        [Test]
        public void FirstBudgetMovesTwentyGiftCoinsAndFirstPurchasesDoesNotSpendThem()
        {
            var budget = FinikQuestCatalog.Quests.Single(q => q.Id == FinikQuestEngine.FirstBudgetQuestId);
            var purchases = FinikQuestCatalog.Quests.Single(q => q.Id == "d1-first-purchases");

            Assert.That(FinikQuestEngine.AutoSaveAfterQuestion(budget, 0), Is.EqualTo(20));
            Assert.That(FinikQuestEngine.AutoSaveAfterQuestion(budget, 1), Is.Zero);
            Assert.That(budget.Questions[0].Choices.All(c => c.Save == 0), Is.True, "auto-save must not be duplicated by a choice");
            Assert.That(purchases.Questions.SelectMany(q => q.Choices).All(c => c.Spend == 0), Is.True,
                "«Первые покупки» must open the shop with the remaining 10 coins, not pre-spend them");
            Assert.That(FinikWalletEngine.DailyLoginIncome - FinikQuestEngine.FirstBudgetAutoSave, Is.EqualTo(10));
        }

        [Test]
        public void FirstBudgetRiskResponseDoesNotSpeakACharacterNamePlaceholder()
        {
            var budget = FinikQuestCatalog.Quests.Single(q => q.Id == FinikQuestEngine.FirstBudgetQuestId);
            var response = budget.Questions[1].Choices.Single(c => c.Id == "q2-c2");
            Assert.That(response.Explanation, Is.EqualTo("Если не тратить на нужное, питомец проголодается."));

            var clip = Resources.Load<AudioClip>("Audio/voice/quests/day1/d1-first-budget_q2_q2-c2");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.GreaterThan(1f));
        }

        [Test]
        public void DayThreeFollowsWorksheetAndOnlyThePlanningLessonMovesSavings()
        {
            var quests = FinikQuestCatalog.Quests.Where(q => q.Day == 3).OrderBy(q => q.Order).ToArray();
            Assert.That(quests.Select(q => q.Title), Is.EqualTo(new[]
            {
                "Переходим от букв к цифрам!", "Цель всё ближе", "Безопасная покупка"
            }));
            Assert.That(quests.Select(q => q.Questions.Length), Is.EqualTo(new[] { 3, 3, 1 }));
            Assert.That(quests[0].Questions[0].Prompt, Does.Contain("тетрадь"));
            Assert.That(quests[0].Questions[2].Choices[0].Save, Is.EqualTo(40));
            Assert.That(quests[1].Questions.SelectMany(q => q.Choices).All(c => c.Withdraw == 0), Is.True);
            Assert.That(quests[2].Questions[0].Prompt, Does.Contain("реальные деньги"));
        }

        [Test]
        public void DayFourFollowsWorksheetAndOpensAfterTheThirdDay()
        {
            var quests = FinikQuestCatalog.Quests.Where(q => q.Day == 4).OrderBy(q => q.Order).ToArray();
            Assert.That(quests.Select(q => q.Title), Is.EqualTo(new[]
            {
                "Новый день - новый план!", "Что там с копилкой?", "Срочная покупка"
            }));
            Assert.That(quests.Select(q => q.Questions.Length), Is.EqualTo(new[] { 3, 3, 1 }));
            Assert.That(quests[0].Questions[0].Prompt, Does.Contain("3 монетки"));
            Assert.That(quests[1].Questions[2].Prompt, Does.Contain("электронном виде"));
            Assert.That(quests[2].Questions[0].Prompt, Does.Contain("лекарство"));

            var tasks = FinikQuestEngine.BuildTasks(DayOne.AddDays(3), 4);
            Assert.That(tasks.Where(t => !t.IsWeekly).Select(t => t.Quest.Id), Is.EqualTo(quests.Select(q => q.Id)));
        }

        [Test]
        public void DayFourQuestionVoicesAreShippedInResources()
        {
            foreach (var quest in FinikQuestCatalog.Quests.Where(q => q.Day == 4))
            {
                for (int question = 1; question <= quest.Questions.Length; question++)
                {
                    var clip = Resources.Load<AudioClip>($"Audio/voice/quests/day4/{quest.Id}_q{question}_prompt");
                    Assert.That(clip, Is.Not.Null, $"Missing voice for {quest.Id} question {question}");
                    Assert.That(clip.length, Is.GreaterThan(1f), $"Voice is unexpectedly short: {quest.Id} question {question}");
                }
            }
        }

        [Test]
        public void DayFiveFollowsWorksheetAndHasNineQuestions()
        {
            var quests = FinikQuestCatalog.Quests.Where(q => q.Day == 5).OrderBy(q => q.Order).ToArray();
            Assert.That(quests.Select(q => q.Title), Is.EqualTo(new[]
            {
                "Считаем и планируем", "Копилка растёт!", "Проверяем покупку"
            }));
            Assert.That(quests.Select(q => q.Questions.Length), Is.EqualTo(new[] { 3, 3, 3 }));
            Assert.That(quests[0].Questions[2].Prompt, Does.Contain("тетрадь"));
            Assert.That(quests[1].Questions[1].Choices[0].Label, Is.EqualTo("2 монетки"));
            Assert.That(quests[1].Questions[1].Choices[0].Tag, Is.EqualTo(FinikQuestTag.Good));
            Assert.That(quests[2].Questions[2].Prompt, Does.Contain("сдачу и чек"));

            var tasks = FinikQuestEngine.BuildTasks(DayOne.AddDays(4), 5);
            Assert.That(tasks.Where(t => !t.IsWeekly).Select(t => t.Quest.Id), Is.EqualTo(quests.Select(q => q.Id)));
        }

        [Test]
        public void DayFiveQuestionVoicesAreShippedInResources()
        {
            foreach (var quest in FinikQuestCatalog.Quests.Where(q => q.Day == 5))
            {
                for (int question = 1; question <= quest.Questions.Length; question++)
                {
                    var clip = Resources.Load<AudioClip>($"Audio/voice/quests/day5/{quest.Id}_q{question}_prompt");
                    Assert.That(clip, Is.Not.Null, $"Missing voice for {quest.Id} question {question}");
                    Assert.That(clip.length, Is.GreaterThan(1f), $"Voice is unexpectedly short: {quest.Id} question {question}");
                }
            }
        }

        [Test]
        public void CampaignDayStartsAtOneAndCapsAtFive()
        {
            var state = new FinikQuestState { campaignStartDayKey = "2026-09-20" };
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne), Is.EqualTo(1));
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne.AddDays(1)), Is.EqualTo(2));
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne.AddDays(2)), Is.EqualTo(3));
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne.AddDays(3)), Is.EqualTo(4));
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne.AddDays(4)), Is.EqualTo(5));
            Assert.That(FinikQuestEngine.CampaignDay(state, DayOne.AddDays(20)), Is.EqualTo(5));
        }

        [Test]
        public void EconomyUsesThirtyCoinDailyIncomeAndRequestedGoalPrices()
        {
            Assert.That(FinikWalletEngine.DailyLoginIncome, Is.EqualTo(30));
            Assert.That(FinikQuestCatalog.DailyReward(1), Is.EqualTo(10));
            Assert.That(FinikGoalCatalog.Goals.Select(g => g.Price), Is.EqualTo(new[] { 80, 90, 100, 110 }));
        }

        [Test]
        public void PositiveRouteKeepsFiveDayEconomyConsistent()
        {
            var asset = Resources.Load<TextAsset>(FinikQuestCatalog.ResourcePath);
            var problems = FinikQuestCatalog.Parse(asset.text, out var content);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));

            int wallet = 0;
            int savings = 0;
            int claimedDaily = 0;
            bool weeklyPaid = false;
            // Day one moves 20 of the gifted 30 coins into savings. Day four uses two
            // saved coins for medicine, while the streak remains a zero-coin milestone.
            var expectedDayStartWallet = new[] { 30, 60, 120, 140, 202 };
            var expectedDayStartSavings = new[] { 0, 20, 20, 60, 58 };

            for (int day = 1; day <= 5; day++)
            {
                wallet += FinikWalletEngine.DailyLoginIncome;
                Assert.That(wallet, Is.EqualTo(expectedDayStartWallet[day - 1]), $"day {day} start wallet");
                Assert.That(savings, Is.EqualTo(expectedDayStartSavings[day - 1]), $"day {day} start savings");

                foreach (var quest in content.quests.Where(q => q.Day == day).OrderBy(q => q.Order))
                {
                    for (int questionIndex = 0; questionIndex < quest.Questions.Length; questionIndex++)
                    {
                        var question = quest.Questions[questionIndex];
                        var choice = question.Choices.First(c => c.Tag == FinikQuestTag.Good);
                        wallet += choice.Withdraw;
                        savings -= choice.Withdraw;
                        wallet -= choice.Save;
                        savings += choice.Save;
                        wallet -= choice.Spend;
                        int autoSave = FinikQuestEngine.AutoSaveAfterQuestion(quest, questionIndex);
                        wallet -= autoSave;
                        savings += autoSave;
                    }
                    wallet += FinikQuestEngine.RewardFor(quest);
                    claimedDaily++;
                    if (!weeklyPaid && claimedDaily >= content.weekly.Target)
                    {
                        wallet += content.weekly.Reward;
                        weeklyPaid = true;
                    }
                }
            }

            Assert.That(weeklyPaid, Is.True, "the streak bonus was never reachable");
            Assert.That(wallet, Is.EqualTo(232));
            Assert.That(savings, Is.EqualTo(58));

            // The same arithmetic the shipped balance model walks.
            var route = FinikEconomyBalance.PositiveRoute();
            Assert.That(route[^1].wallet, Is.EqualTo(wallet));
            Assert.That(route[^1].savings, Is.EqualTo(savings));
        }

        [Test]
        public void SyncReplacesYesterdayScenarios()
        {
            // The board is the day's quests plus the weekly streak task that closes it.
            int boardSize = FinikQuestCatalog.DailyCount + 1;
            var state = new FinikQuestState { campaignStartDayKey = "2026-09-20" };
            var day1Tasks = FinikQuestEngine.BuildTasks(DayOne, 1);
            Assert.That(day1Tasks.Count, Is.EqualTo(boardSize));
            Assert.That(FinikQuestEngine.Sync(state, day1Tasks, DayOne, 1), Is.True);
            Assert.That(state.progress.Count, Is.EqualTo(boardSize));

            state.Find(day1Tasks[0].Id).status = FinikQuestStatus.Claimed;
            var day2 = DayOne.AddDays(1);
            var day2Tasks = FinikQuestEngine.BuildTasks(day2, 2);
            Assert.That(FinikQuestEngine.Sync(state, day2Tasks, day2, 2), Is.True);
            Assert.That(state.progress.Count, Is.EqualTo(boardSize));
            Assert.That(state.Find(day1Tasks[0].Id), Is.Null);
            Assert.That(state.progress.All(p => p.status == FinikQuestStatus.Available), Is.True);
        }

        [MenuItem("Finik/Testing/Quest Campaign/Load Day 1")]
        static void LoadDay1() => LoadDay(1);

        [MenuItem("Finik/Testing/Quest Campaign/Load Day 2")]
        static void LoadDay2() => LoadDay(2);

        [MenuItem("Finik/Testing/Quest Campaign/Load Day 3")]
        static void LoadDay3() => LoadDay(3);

        [MenuItem("Finik/Testing/Quest Campaign/Load Day 4")]
        static void LoadDay4() => LoadDay(4);

        [MenuItem("Finik/Testing/Quest Campaign/Load Day 5")]
        static void LoadDay5() => LoadDay(5);

        static void LoadDay(int day)
        {
            FinikGame.StartCampaignDayForTesting(day);
            Debug.Log($"[FinikQuestQA] Loaded day {day}: wallet={FinikGame.Balance}, savings={FinikGame.Savings}, goal={(FinikGame.HasSelectedGoal ? FinikGame.Goal.Title : "not selected")}");
        }
    }
}
