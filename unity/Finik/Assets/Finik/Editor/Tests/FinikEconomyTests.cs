using System.Linq;
using Finik.Core;
using Finik.UI.Onboarding;
using Finik.UI.Savings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Editor.Tests
{
    /// <summary>
    /// The pacing contract: a child reaches a savings goal in two to four days, and the shop still
    /// forces a choice between «Нужно» and «Хочу». Guards the shipped catalogs, not a fixture.
    /// </summary>
    public sealed class FinikEconomyTests
    {
        [Test]
        public void ShippedEconomyHoldsItsBalanceContract()
        {
            var problems = FinikEconomyBalance.Validate();
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryGoalIsReachableInTwoToFourDays()
        {
            foreach (var goal in FinikGoalCatalog.Goals)
            {
                int? days = FinikEconomyBalance.DaysToAfford(goal.Price);
                Assert.That(days, Is.Not.Null, $"«{goal.Title}» ({goal.Price}) недостижима");
                Assert.That(days.Value, Is.InRange(FinikEconomyBalance.DaysMin, FinikEconomyBalance.DaysMax),
                    $"«{goal.Title}» ({goal.Price}) закрывается за {days} дн.");
            }
        }

        [Test]
        public void PositiveRouteNeverGoesNegativeAndStartsWithTwentySaved()
        {
            var route = FinikEconomyBalance.PositiveRoute();
            Assert.That(route, Is.Not.Empty);
            Assert.That(route.Select(d => d.day), Is.EqualTo(Enumerable.Range(1, route.Count)));
            Assert.That(route[0].wallet, Is.EqualTo(30));
            Assert.That(route[0].savings, Is.EqualTo(20));

            for (int i = 0; i < route.Count; i++)
            {
                Assert.That(route[i].wallet, Is.GreaterThanOrEqualTo(0), $"день {route[i].day}: кошелёк ушёл в минус");
                Assert.That(route[i].savings, Is.GreaterThanOrEqualTo(20), $"день {route[i].day}: первые 20 монет потерялись из копилки");
                if (i > 0 && route[i].day != 4)
                    Assert.That(route[i].savings, Is.GreaterThanOrEqualTo(route[i - 1].savings),
                        $"день {route[i].day}: копилка уменьшилась");
            }

            var urgentPurchaseDay = route.Single(d => d.day == 4);
            Assert.That(urgentPurchaseDay.savings, Is.EqualTo(58),
                "день 4 берёт две монеты из копилки на лекарство");
        }

        [Test]
        public void CheapestGoalNeedsMoreThanASingleDay()
        {
            int cheapest = FinikGoalCatalog.Goals.Min(g => g.Price);
            var firstDay = FinikEconomyBalance.PositiveRoute().First();
            Assert.That(firstDay.Reachable, Is.LessThan(cheapest),
                "самая дешёвая цель закрывается в первый же день — накопление теряет смысл");
        }

        [Test]
        public void TheStreakIsReachableButPaysNoCoins()
        {
            var weekly = FinikQuestCatalog.Weekly;
            Assert.That(weekly, Is.Not.Null);
            Assert.That(weekly.Reward, Is.Zero);
            Assert.That(FinikEconomyBalance.WeeklyBonus, Is.Zero);

            int dailyQuests = FinikQuestCatalog.Quests.Count;
            Assert.That(dailyQuests, Is.GreaterThanOrEqualTo(weekly.Target),
                "серию нельзя закрыть: квестов меньше, чем требует цель");
        }

        [Test]
        public void ShopPricesForceAChoice()
        {
            var needs = FinikShopCatalog.ItemsIn(FinikShopCategory.Need).Where(i => !i.IsLink).ToArray();
            var wants = FinikShopCatalog.ItemsIn(FinikShopCategory.Want).Where(i => !i.IsLink).ToArray();
            Assert.That(needs, Is.Not.Empty);
            Assert.That(wants, Is.Not.Empty);
            Assert.That(needs.All(i => i.Price >= FinikEconomyBalance.NeedPriceMin && i.Price <= FinikEconomyBalance.NeedPriceMax), Is.True);
            Assert.That(wants.All(i => i.Price >= FinikEconomyBalance.WantPriceMin && i.Price <= FinikEconomyBalance.WantPriceMax), Is.True);
            Assert.That(wants.Sum(i => i.Price), Is.GreaterThan(FinikEconomyBalance.MaxDayIncome),
                "за один день нельзя купить все «Хочу»");
            Assert.That(wants.Any(i => i.Price >= FinikEconomyBalance.BigWantPrice), Is.True);
        }

        [Test]
        public void GoalPurchaseConsumesSavingsOnceAndPersists()
        {
            FinikGame.Clear();
            try
            {
                var goal = FinikGoalCatalog.Goals[0];
                FinikGame.StartDemo();
                Assert.That(FinikGame.SelectGoal(goal.Id), Is.True);
                Assert.That(FinikGame.Credit(goal.Price, FinikTransactionSource.Demo, "goal purchase test"), Is.True);
                Assert.That(FinikGame.SaveToGoal(goal.Price), Is.EqualTo(FinikSavingsFailure.None));
                int spendableBefore = FinikGame.Balance;

                Assert.That(FinikGame.CanPurchaseCurrentGoal, Is.True);
                Assert.That(FinikGame.PurchaseCurrentGoal(), Is.True);
                Assert.That(FinikGame.Savings, Is.Zero);
                Assert.That(FinikGame.Balance, Is.EqualTo(spendableBefore));
                Assert.That(FinikGame.GoalPurchased(goal.Id), Is.True);
                Assert.That(FinikGame.PurchaseCurrentGoal(), Is.False);

                FinikGame.Reload();
                Assert.That(FinikGame.GoalPurchased(goal.Id), Is.True);
            }
            finally
            {
                FinikGame.Clear();
            }
        }

        [Test]
        public void RewardPanelFindsPurchaseButtonInsideItsVisualContainer()
        {
            var host = new GameObject("SavingsTest");
            try
            {
                var screen = host.AddComponent<FinikSavingsScreen>();
                var confirm = new GameObject("Confirm", typeof(RectTransform), typeof(CanvasGroup), typeof(FinikScreenPanel));
                confirm.transform.SetParent(host.transform, false);
                var buttons = new GameObject("Buttons", typeof(RectTransform));
                buttons.transform.SetParent(confirm.transform, false);
                var take = new GameObject("Take", typeof(RectTransform));
                take.transform.SetParent(buttons.transform, false);
                var body = new GameObject("Body", typeof(RectTransform), typeof(Button));
                body.transform.SetParent(take.transform, false);

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(FinikSavingsScreen).GetField("confirm", flags).SetValue(screen, confirm.GetComponent<FinikScreenPanel>());
                typeof(FinikSavingsScreen).GetMethod("EnsureRewardPanel", flags).Invoke(screen, null);

                var purchase = (Button)typeof(FinikSavingsScreen).GetField("rewardBuyButton", flags).GetValue(screen);
                Assert.That(purchase, Is.Not.Null);
                Assert.That(purchase.transform.parent.name, Is.EqualTo("Take"));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void DemoCompletionKeepsWalletAndGoalSavingsInSync()
        {
            FinikGame.Clear();
            try
            {
                FinikGame.StartDemo();
                Assert.That(FinikGame.CompleteDemoDay(maxQuests: 1), Is.True);
                Assert.That(FinikGame.DemoDayComplete, Is.False);
                Assert.That(FinikGame.CompleteDemoDay(maxQuests: 1), Is.True);
                Assert.That(FinikGame.DemoDayComplete, Is.False);
                Assert.That(FinikGame.CompleteDemoDay(maxQuests: 1), Is.True);
                Assert.That(FinikGame.Balance, Is.EqualTo(30));
                Assert.That(FinikGame.Savings, Is.EqualTo(20));
                Assert.That(FinikGame.DemoDayComplete, Is.True);
                Assert.That(FinikGame.CompleteDemoDay(), Is.False);

                FinikGame.StartDemoCampaignDay(2);
                Assert.That(FinikGame.CompleteDemoDay(), Is.True);
                FinikGame.StartDemoCampaignDay(3);
                Assert.That(FinikGame.CompleteDemoDay(), Is.True);
                Assert.That(FinikGame.Balance, Is.EqualTo(110));
                Assert.That(FinikGame.Savings, Is.EqualTo(60));

                FinikGame.Reload();
                Assert.That(FinikGame.Balance, Is.EqualTo(110));
                Assert.That(FinikGame.Savings, Is.EqualTo(60));
            }
            finally
            {
                FinikGame.Clear();
            }
        }

        [Test]
        public void EveryGoalHasAThreeDimensionalRoomReward()
        {
            foreach (var goal in FinikGoalCatalog.Goals)
            {
                string root = $"Assets/Finik/Resources/GoalRewards/{goal.Id}";
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>($"{root}/model.obj"), Is.Not.Null,
                    $"{goal.Id}: 3D model is missing");
                Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/texture.png"), Is.Not.Null,
                    $"{goal.Id}: texture is missing");
            }
        }
    }
}
