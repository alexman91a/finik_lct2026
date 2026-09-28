using System;
using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using NUnit.Framework;

namespace Finik.Editor.Tests
{
    public sealed class FinikMatch3Tests
    {
        const int W = 6, H = 6;
        /// <summary>The colour the tests build their matches from; the background never uses it.</summary>
        const int Hero = 4;

        /// <summary>A background with no run and no square in it, in colours 0..3.</summary>
        static int[] Background(int w = W, int h = H)
        {
            var colors = new int[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                colors[x + y * w] = (x + 2 * y) % 4;
            return colors;
        }

        static FinikMatch3Model Board(int[] colors, FinikMatch3Layout layout = null, FinikMatch3Special[] specials = null)
        {
            var model = new FinikMatch3Model(layout ?? FinikMatch3Layout.Rectangle(W, H, 5, new FinikMatch3Goal(FinikMatch3GoalKind.Gems, 999)), 7);
            model.Load(colors, specials);
            return model;
        }

        static int At(int x, int y, int w = W) => x + y * w;

        static FinikMatch3Blast FirstBlast(FinikMatch3Turn turn) => turn.Steps.OfType<FinikMatch3Blast>().First();

        static void AssertMade(FinikMatch3Turn turn, FinikMatch3Special special, int cell)
        {
            Assert.That(turn, Is.Not.Null, "the swap should work");
            var created = FirstBlast(turn).Created;
            Assert.That(created.Select(c => (c.Special, c.Cell)), Is.EquivalentTo(new[] { (special, cell) }));
        }

        // ------------------------------------------------------------------ powers from matches

        [Test]
        public void FourInARowMakesARocketWhereTheGemLanded()
        {
            var colors = Background();
            colors[At(0, 2)] = colors[At(1, 2)] = colors[At(3, 2)] = Hero;
            colors[At(2, 3)] = Hero;
            var model = Board(colors);
            AssertMade(model.Swap(At(2, 3), At(2, 2)), FinikMatch3Special.RocketH, At(2, 2));
        }

        [Test]
        public void SquareMakesAPlane()
        {
            var colors = Background();
            colors[At(1, 1)] = colors[At(2, 1)] = colors[At(1, 2)] = Hero;
            colors[At(3, 2)] = Hero;
            var model = Board(colors);
            AssertMade(model.Swap(At(3, 2), At(2, 2)), FinikMatch3Special.Plane, At(2, 2));
        }

        [Test]
        public void CornerMakesABomb()
        {
            var colors = Background();
            colors[At(2, 0)] = colors[At(2, 1)] = Hero;
            colors[At(3, 2)] = colors[At(4, 2)] = Hero;
            colors[At(1, 2)] = Hero;
            var model = Board(colors);
            // The gem from the left completes the column above and the row to the right at once.
            AssertMade(model.Swap(At(1, 2), At(2, 2)), FinikMatch3Special.Bomb, At(2, 2));
        }

        [Test]
        public void FiveInARowMakesADiscoBall()
        {
            var colors = Background();
            colors[At(0, 2)] = colors[At(1, 2)] = colors[At(3, 2)] = colors[At(4, 2)] = Hero;
            colors[At(2, 3)] = Hero;
            var model = Board(colors);
            AssertMade(model.Swap(At(2, 3), At(2, 2)), FinikMatch3Special.Rainbow, At(2, 2));
        }

        [Test]
        public void AUselessSwapIsRefusedAndChangesNothing()
        {
            var model = Board(Background());
            var before = Enumerable.Range(0, W * H).Select(i => model.PieceAt(i).Id).ToArray();
            Assert.That(model.Swap(At(0, 0), At(1, 0)), Is.Null);
            Assert.That(Enumerable.Range(0, W * H).Select(i => model.PieceAt(i).Id), Is.EqualTo(before));
        }

        // ------------------------------------------------------------------ powers going off

        [Test]
        public void DiscoBallSwappedWithAGemClearsThatColour()
        {
            var specials = new FinikMatch3Special[W * H];
            specials[At(0, 0)] = FinikMatch3Special.Rainbow;
            var colors = Background();
            var model = Board(colors, specials: specials);
            int color = model.PieceAt(At(1, 0)).Color;
            int onBoard = Enumerable.Range(0, W * H).Count(i => model.PieceAt(i).IsGem && model.PieceAt(i).Color == color);

            var turn = model.Swap(At(0, 0), At(1, 0));
            var cleared = FirstBlast(turn).Removed.Where(r => r.Special == FinikMatch3Special.None).ToList();
            Assert.That(cleared.Count, Is.EqualTo(onBoard));
            Assert.That(cleared.All(r => r.Color == color));
        }

        [Test]
        public void TappedRocketClearsItsWholeRow()
        {
            var specials = new FinikMatch3Special[W * H];
            specials[At(3, 4)] = FinikMatch3Special.RocketH;
            var model = Board(Background(), specials: specials);
            var turn = model.Tap(At(3, 4));
            var cells = FirstBlast(turn).Removed.Select(r => r.Cell).ToList();
            Assert.That(cells, Is.SupersetOf(Enumerable.Range(0, W).Select(x => At(x, 4))));
        }

        [Test]
        public void RocketSetsOffABombInItsWay()
        {
            var specials = new FinikMatch3Special[W * H];
            specials[At(0, 3)] = FinikMatch3Special.RocketH;
            specials[At(4, 3)] = FinikMatch3Special.Bomb;
            var model = Board(Background(), specials: specials);
            var blast = FirstBlast(model.Tap(At(0, 3)));
            // The bomb's reach two rows up and down proves it went off.
            Assert.That(blast.Removed.Select(r => r.Cell), Is.SupersetOf(new[] { At(4, 1), At(4, 5) }));
            Assert.That(blast.Effects.Any(e => e.Kind == FinikMatch3EffectKind.Bomb));
        }

        [Test]
        public void TwoRocketsSwappedTogetherClearACross()
        {
            var specials = new FinikMatch3Special[W * H];
            specials[At(2, 2)] = FinikMatch3Special.RocketH;
            specials[At(3, 2)] = FinikMatch3Special.RocketV;
            var model = Board(Background(), specials: specials);
            var cells = FirstBlast(model.Swap(At(2, 2), At(3, 2))).Removed.Select(r => r.Cell).ToList();
            Assert.That(cells, Is.SupersetOf(Enumerable.Range(0, W).Select(x => At(x, 2))));
            Assert.That(cells, Is.SupersetOf(Enumerable.Range(0, H).Select(y => At(3, y))));
        }

        // ------------------------------------------------------------------ obstacles and gravity

        [Test]
        public void AMatchNextToACrateTakesOneLayer()
        {
            var crates = new byte[W * H];
            crates[At(2, 4)] = 2;
            var layout = new FinikMatch3Layout(W, H, 5, null, crates, null, new[] { new FinikMatch3Goal(FinikMatch3GoalKind.Crate, 1) });
            var colors = Background();
            colors[At(0, 3)] = colors[At(1, 3)] = Hero;
            colors[At(2, 2)] = Hero;
            var model = Board(colors, layout);
            var turn = model.Swap(At(2, 2), At(2, 3));
            Assert.That(turn, Is.Not.Null);
            Assert.That(model.CrateAt(At(2, 4)), Is.EqualTo(1));
            Assert.That(model.Collected(0), Is.Zero);
        }

        [Test]
        public void CellsUnderACrateAreFilledFromTheSide()
        {
            var crates = new byte[W * H];
            crates[At(2, 1)] = 1;
            var layout = new FinikMatch3Layout(W, H, 5, null, crates, null, new[] { new FinikMatch3Goal(FinikMatch3GoalKind.Gems, 999) });
            var model = Board(Background(), layout);
            model.Strike(new[] { At(2, 4), At(2, 3) });
            for (int i = 0; i < W * H; i++)
                if (model.CrateAt(i) == 0) Assert.That(model.PieceAt(i).Exists, $"cell {i % W},{i / W} left empty");
            Assert.That(model.CrateAt(At(2, 1)), Is.EqualTo(1), "a strike below the crate must not touch it");
        }

        [Test]
        public void PiecesFallPastAHole()
        {
            var open = Enumerable.Repeat(true, W * H).ToArray();
            open[At(2, 2)] = false;
            var layout = new FinikMatch3Layout(W, H, 5, open, null, null, new[] { new FinikMatch3Goal(FinikMatch3GoalKind.Gems, 999) });
            var model = Board(Background(), layout);
            int above = model.PieceAt(At(2, 1)).Id;
            model.Strike(new[] { At(2, 3) });
            Assert.That(model.PieceAt(At(2, 2)).Exists, Is.False, "nothing may stand in a hole");
            Assert.That(model.PieceAt(At(2, 3)).Id, Is.EqualTo(above), "the gem above the hole drops into the gap below it");
        }

        [Test]
        public void IceMeltsWhenTheGemOnItClears()
        {
            var ice = new byte[W * H];
            ice[At(1, 2)] = 1;
            var layout = new FinikMatch3Layout(W, H, 5, null, null, ice, new[] { new FinikMatch3Goal(FinikMatch3GoalKind.Ice, 1) });
            var colors = Background();
            colors[At(0, 2)] = colors[At(1, 2)] = Hero;
            colors[At(2, 3)] = Hero;
            var model = Board(colors, layout);
            Assert.That(model.Swap(At(2, 3), At(2, 2)), Is.Not.Null);
            Assert.That(model.IceAt(At(1, 2)), Is.Zero);
            Assert.That(model.Won, Is.True);
        }

        [Test]
        public void AGemCountsForItsColourFirstAndOnlyOnce()
        {
            var layout = FinikMatch3Layout.Rectangle(W, H, 5,
                new FinikMatch3Goal(FinikMatch3GoalKind.Gems, 50), new FinikMatch3Goal(FinikMatch3GoalKind.Color, 2, Hero));
            var colors = Background();
            colors[At(0, 2)] = colors[At(1, 2)] = Hero;
            colors[At(2, 3)] = Hero;
            var model = Board(colors, layout);
            model.Swap(At(2, 3), At(2, 2));
            // Three hero gems: two fill the colour goal, the third spills over into "any gem".
            Assert.That(model.Collected(1), Is.EqualTo(2));
            Assert.That(model.Collected(0), Is.GreaterThanOrEqualTo(1));
            // Every cleared gem, cascades included, lands in exactly one of the two goals.
            Assert.That(model.Collected(0) + model.Collected(1), Is.EqualTo(model.GemsCleared));
        }

        // ------------------------------------------------------------------ shuffle and dead boards

        [Test]
        public void ShuffleKeepsThePiecesAndLeavesAMove()
        {
            var model = new FinikMatch3Model(FinikMatch3Layout.Rectangle(7, 8, 5, new FinikMatch3Goal(FinikMatch3GoalKind.Gems, 999)), 3);
            model.Deal();
            var before = Enumerable.Range(0, 56).Select(i => model.PieceAt(i)).OrderBy(p => p.Id).Select(p => (p.Id, p.Color)).ToArray();
            var turn = model.ShufflePower();
            var after = Enumerable.Range(0, 56).Select(i => model.PieceAt(i)).OrderBy(p => p.Id).Select(p => (p.Id, p.Color)).ToArray();
            Assert.That(after, Is.EqualTo(before), "a shuffle moves pieces, it does not repaint them");
            Assert.That(turn.Steps.OfType<FinikMatch3Shuffle>().Single().Automatic, Is.False);
            Assert.That(model.HasMove(), Is.True);
        }

        [Test]
        public void TheBoardNeverStaysWithoutAMove()
        {
            var random = new Random(11);
            foreach (int number in new[] { 1, 4, 7, 14, 18, 33 })
            {
                var level = (FinikMatch3Level)FinikMatch3Generator.Level(number);
                var layout = level.Setup(level.Rows).Layout;
                for (int game = 0; game < 3; game++)
                {
                    var model = new FinikMatch3Model(layout, random.Next());
                    model.Deal();
                    for (int move = 0; move < 60 && !model.Won; move++)
                    {
                        var options = model.Options();
                        Assert.That(options, Is.Not.Empty, $"level {number}: stuck after {move} moves");
                        Assert.That(model.Play(options[random.Next(options.Count)]), Is.Not.Null);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ generator

        [Test]
        public void LevelsAreTheSameEveryTime()
        {
            var first = (FinikMatch3Level)FinikMatch3Generator.Level(23);
            var layoutA = first.Setup(first.Rows).Layout;
            var layoutB = FinikMatch3Generator.Build(first.Plan, first.Rows);
            Assert.That(layoutB.Open, Is.EqualTo(layoutA.Open));
            Assert.That(layoutB.Crates, Is.EqualTo(layoutA.Crates));
            Assert.That(layoutB.Ice, Is.EqualTo(layoutA.Ice));
        }

        [Test]
        public void DifficultyGoesRoundInFives()
        {
            Assert.That(Enumerable.Range(1, 13).Select(FinikMatch3Generator.TierFor),
                Is.EqualTo(new[] { 1, 2, 3, 1, 2, 1, 2, 3, 1, 2, 1, 2, 3 }));
            Assert.That(FinikMatch3Generator.Level(8).Mood, Is.EqualTo(9));
            Assert.That(FinikMatch3Generator.Level(9).Mood, Is.EqualTo(3));
        }

        [Test]
        public void GeneratedLevelsAreBeatableWithFairMoves()
        {
            for (int number = 1; number <= 30; number++)
            {
                var level = (FinikMatch3Level)FinikMatch3Generator.Level(number);
                foreach (int rows in new[] { level.Rows, level.MaxRows })
                {
                    var setup = level.Setup(rows);
                    Assert.That(setup.BotWinRate, Is.GreaterThanOrEqualTo(0.75f), $"level {number}, {rows} rows");
                    Assert.That(setup.Moves, Is.InRange(12, 45), $"level {number}, {rows} rows");
                    foreach (var goal in setup.Layout.Goals)
                    {
                        if (goal.Kind == FinikMatch3GoalKind.Crate) Assert.That(goal.Count, Is.EqualTo(setup.Layout.CountCrates()));
                        if (goal.Kind == FinikMatch3GoalKind.Ice) Assert.That(goal.Count, Is.EqualTo(setup.Layout.CountIce()));
                        Assert.That(goal.Count, Is.GreaterThan(0), $"level {number}: empty goal {goal.Kind}");
                    }
                    Assert.That(string.IsNullOrEmpty(level.Goal), Is.False);
                }
            }
        }

        [Test]
        public void HubOffersTheNextLevelOfTheEndlessGame()
        {
            Assert.That(FinikMiniGameCatalog.TryGet(FinikMiniGameId.Match3, out var match3));
            Assert.That(FinikMiniGameCatalog.TryGet(FinikMiniGameId.Memory, out var memory));
            Assert.That(match3.Endless && !memory.Endless);
            Assert.That(match3.HubLevels(0, 3), Is.EqualTo(new[] { 1 }));
            Assert.That(match3.HubLevels(12, 3), Is.EqualTo(new[] { 13 }));
            Assert.That(memory.HubLevels(12, 3), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(match3.HasLevel(500) && !memory.HasLevel(4));
            Assert.That(match3.Level(118).Title, Is.EqualTo("Ур. 118"));
        }
    }
}
