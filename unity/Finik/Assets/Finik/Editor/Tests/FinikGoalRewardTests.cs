using Finik.Core;
using NUnit.Framework;
using UnityEngine;

namespace Finik.Editor.Tests
{
    public sealed class FinikGoalRewardTests
    {
        [SetUp]
        public void SetUp()
        {
            FinikGame.Clear();
            FinikGame.StartDemo();
        }

        [TearDown]
        public void TearDown() => FinikGame.Clear();

        [Test]
        public void ReachedGoalCanBeBoughtOnceAndPersists()
        {
            Assert.That(FinikGame.SelectGoal("blocks"), Is.True);
            FinikGame.AdjustDemoCoins(100);

            var goal = FinikGame.Goal;
            Assert.That(goal.Id, Is.EqualTo("blocks"));
            Assert.That(FinikGame.SaveToGoal(goal.Price), Is.EqualTo(FinikSavingsFailure.None));
            Assert.That(FinikGame.CanPurchaseCurrentGoal, Is.True);
            int balanceAfterSaving = FinikGame.Balance;
            Assert.That(FinikGame.Savings, Is.EqualTo(goal.Price));

            Assert.That(FinikGame.PurchaseCurrentGoal(), Is.True);
            Assert.That(FinikGame.Balance, Is.EqualTo(balanceAfterSaving),
                "Buying the saved-for goal must not charge spendable coins a second time.");
            Assert.That(FinikGame.Savings, Is.Zero);
            Assert.That(FinikGame.GoalPurchased("blocks"), Is.True);
            Assert.That(FinikGame.PurchaseCurrentGoal(), Is.False);

            FinikGame.Reload();
            Assert.That(FinikGame.GoalPurchased("blocks"), Is.True);
            Assert.That(FinikGame.SelectGoal("blocks"), Is.False,
                "A room reward is one-time; bought goals cannot be selected again.");
        }

        [TestCase("bike")]
        [TestCase("headphones")]
        [TestCase("blocks")]
        [TestCase("skateboard")]
        public void GoalRewardModelIsAvailableToRuntime(string id)
        {
            var model = Resources.Load<GameObject>($"GoalRewards/{id}/model");
            Assert.That(model, Is.Not.Null, $"Missing Resources goal model: {id}");
            Assert.That(model.GetComponentInChildren<MeshFilter>(true), Is.Not.Null, $"{id}: no mesh");
            Assert.That(model.GetComponentInChildren<Renderer>(true), Is.Not.Null, $"{id}: no renderer");
        }
    }
}
