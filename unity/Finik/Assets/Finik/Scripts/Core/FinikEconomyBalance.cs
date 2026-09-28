using System;
using System.Collections.Generic;
using System.Linq;

namespace Finik.Core
{
    /// <summary>What the three-day campaign leaves in the wallet and the piggy bank at the end of a day.</summary>
    public readonly struct FinikDayBalance
    {
        public readonly int day;
        public readonly int wallet;
        public readonly int savings;

        public FinikDayBalance(int day, int wallet, int savings)
        {
            this.day = day;
            this.wallet = wallet;
            this.savings = savings;
        }

        /// <summary>Everything the child could put towards a goal by the end of this day.</summary>
        public int Reachable => wallet + savings;
    }

    /// <summary>
    /// The pacing contract of the economy: a child must be able to reach a savings goal in
    /// <see cref="DaysMin"/>–<see cref="DaysMax"/> days, while still having to choose between
    /// «Нужно», «Хочу» and «Коплю».
    ///
    /// Everything here is derived from the shipped catalogs, so changing a goal price, a quest reward
    /// or the daily income can never silently break the pacing: <see cref="Validate"/> and the tests
    /// around it fail instead.
    /// </summary>
    public static class FinikEconomyBalance
    {
        public const int DaysMin = 2;
        public const int DaysMax = 4;

        /// <summary>«Нужно» uses three clear price steps: 3, 4 and 5 coins.</summary>
        public const int NeedPriceMin = 3, NeedPriceMax = 5;
        /// <summary>«Хочу» costs a bit more than «Нужно», in four steps from 3 to 6 coins.</summary>
        public const int WantPriceMin = 3, WantPriceMax = 6;
        /// <summary>At least one want at the top step, so the wants are not all the same.</summary>
        public const int BigWantPrice = 6;

        /// <summary>The most a single campaign day can pay: login income plus every quest of the richest day.</summary>
        public static int MaxDayIncome => DailyIncome + Enumerable.Range(1, Math.Max(1, CampaignDays)).Max(DailyQuestReward);

        public static int DailyIncome => FinikWalletEngine.DailyLoginIncome;

        /// <summary>Days the shipped quest campaign covers.</summary>
        public static int CampaignDays => FinikQuestCatalog.Quests.Count == 0
            ? 0
            : FinikQuestCatalog.Quests.Max(q => q.Day);

        /// <summary>The streak bonus paid once enough daily quests are claimed in a week.</summary>
        public static int WeeklyBonus => FinikQuestCatalog.Weekly?.Reward ?? 0;

        /// <summary>Coins the day's quests pay when all three are played through.</summary>
        public static int DailyQuestReward(int day) => FinikQuestCatalog.Quests
            .Where(q => q.Day == day)
            .Sum(FinikQuestEngine.RewardFor);

        /// <summary>
        /// Walks the campaign the way a child following the best answers does, and reports the wallet
        /// and the piggy bank at the end of every day. The same arithmetic the quest screen performs.
        /// </summary>
        public static IReadOnlyList<FinikDayBalance> PositiveRoute()
        {
            var days = new List<FinikDayBalance>();
            var weekly = FinikQuestCatalog.Weekly;
            int wallet = 0, savings = 0, claimedDaily = 0;
            bool weeklyPaid = false;

            for (int day = 1; day <= CampaignDays; day++)
            {
                wallet += DailyIncome;
                foreach (var quest in FinikQuestCatalog.Quests.Where(q => q.Day == day).OrderBy(q => q.Order))
                {
                    for (int questionIndex = 0; questionIndex < quest.Questions.Length; questionIndex++)
                    {
                        var question = quest.Questions[questionIndex];
                        var choice = question.Choices.FirstOrDefault(c => c.Tag == FinikQuestTag.Good);
                        if (choice == null) continue;
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

                    // The streak bonus is counted once. A campaign that crosses a Monday would earn it
                    // again, so paying it once keeps this a lower bound on what the child can collect.
                    if (!weeklyPaid && weekly != null && weekly.Target > 0 && claimedDaily >= weekly.Target)
                    {
                        wallet += weekly.Reward;
                        weeklyPaid = true;
                    }
                }
                days.Add(new FinikDayBalance(day, wallet, savings));
            }

            return days;
        }

        /// <summary>The first day the child could pay for <paramref name="price"/>, or null if never.</summary>
        public static int? DaysToAfford(int price)
        {
            foreach (var day in PositiveRoute())
                if (day.Reachable >= price) return day.day;
            return null;
        }

        /// <summary>
        /// Deterministic acceptance guard for the whole economy. An empty list means the balance
        /// contract holds.
        /// </summary>
        public static IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var route = PositiveRoute();

            if (route.Count == 0)
            {
                problems.Add("the quest campaign is empty: there is no economy to balance");
                return problems;
            }

            foreach (var goal in FinikGoalCatalog.Goals)
            {
                int? days = DaysToAfford(goal.Price);
                if (days == null)
                {
                    problems.Add($"цель «{goal.Title}» ({goal.Price}) недостижима за {route.Count} дн.: всего можно собрать {route[^1].Reachable}");
                    continue;
                }
                if (days < DaysMin)
                    problems.Add($"цель «{goal.Title}» ({goal.Price}) закрывается уже на {days}-й день — копить не приходится");
                if (days > DaysMax)
                    problems.Add($"цель «{goal.Title}» ({goal.Price}) требует {days} дн., допустимо не больше {DaysMax}");
            }

            var needs = FinikShopCatalog.ItemsIn(FinikShopCategory.Need).Where(i => !i.IsLink && i.Price > 0).ToArray();
            var wants = FinikShopCatalog.ItemsIn(FinikShopCategory.Want).Where(i => !i.IsLink && i.Price > 0).ToArray();

            if (needs.Length == 0) problems.Add("в магазине нет ни одной покупки «Нужно»");
            if (wants.Length == 0) problems.Add("в магазине нет ни одной покупки «Хочу»");
            if (needs.Length == 0 || wants.Length == 0) return problems;

            // Links such as «Еда» are navigation and keep price 0.
            foreach (var item in needs)
                if (item.Price < NeedPriceMin || item.Price > NeedPriceMax)
                    problems.Add($"«Нужно»: «{item.Title}» стоит {item.Price}, допустимо {NeedPriceMin}–{NeedPriceMax}");
            foreach (var item in wants)
                if (item.Price < WantPriceMin || item.Price > WantPriceMax)
                    problems.Add($"«Хочу»: «{item.Title}» стоит {item.Price}, допустимо {WantPriceMin}–{WantPriceMax}");

            // Scarcity (п. 2.1, Q&A п. 8): one day's coins must never buy every want at once.
            int allWants = wants.Sum(i => i.Price);
            if (allWants <= MaxDayIncome)
                problems.Add($"все «Хочу» стоят {allWants}, а за день можно получить {MaxDayIncome}: выбирать не приходится");
            if (!wants.Any(i => i.Price >= BigWantPrice))
                problems.Add($"нет ни одной «Хочу» за {BigWantPrice} монет");

            return problems;
        }
    }
}
