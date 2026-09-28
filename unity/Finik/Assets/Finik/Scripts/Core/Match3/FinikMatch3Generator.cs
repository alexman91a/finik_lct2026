using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Finik.Core
{
    /// <summary>
    /// One level of «три в ряд». Everything the hub needs — number, difficulty, reward — is known at
    /// once; the board itself (shape, crates, ice, goals) and the number of moves are worked out by
    /// <see cref="FinikMatch3Generator"/> the first time they are asked for, and remembered.
    /// </summary>
    public sealed class FinikMatch3Level : FinikMiniGameLevel
    {
        readonly int tier;
        FinikMatch3Generator.Plan plan;
        readonly object gate = new();

        internal FinikMatch3Level(int number, int tier) : base(number, null)
        {
            this.tier = tier;
        }

        public override int Tier => tier;
        /// <summary>The hub chip's name; a three-digit number no longer fits beside the medal in full.</summary>
        public override string Title => Number < 100 ? $"Уровень {Number}" : $"Ур. {Number}";
        public override string Heading => $"Уровень {Number} · {FinikMiniGameCatalog.TitleFor(tier).ToLowerInvariant()}";

        /// <summary>
        /// XP only for the first three levels and for every hard one after them: an endless game must
        /// not turn into an endless source of growth, but a real feat still counts.
        /// </summary>
        public override int Xp => Number <= FinikMiniGameCatalog.LevelCount ? FinikMiniGameCatalog.XpFor(Number)
            : tier >= 3 ? FinikMiniGameCatalog.XpFor(2) : 0;

        public override string Goal => FinikMatch3Generator.Describe(Plan.Goals);

        public int Columns => Plan.Columns;
        /// <summary>Rows the level is designed for; a tall screen may play it with a few more.</summary>
        public int Rows => Plan.BaseRows;
        public int MaxRows => Plan.BaseRows + FinikMatch3Generator.ExtraRows;
        public int Colors => Plan.Colors;
        public IReadOnlyList<FinikMatch3Goal> Goals => Plan.Goals;

        /// <summary>What the level is made of; worked out (and tested by the bot) on first use.</summary>
        public FinikMatch3Generator.Plan Plan
        {
            get
            {
                lock (gate) return plan ??= FinikMatch3Generator.MakePlan(Number, tier);
            }
        }

        /// <summary>The board and the moves for <paramref name="rows"/> rows, worked out and tested by the bot.</summary>
        public FinikMatch3Setup Setup(int rows) => FinikMatch3Generator.Setup(this, rows);

        /// <summary>Rows the last board was dealt with: a good guess for warming up the next level.</summary>
        public static volatile int LastRows;

        /// <summary>
        /// Works the level out in the background ahead of time — the hub calls it for the level the
        /// child is about to pick, so opening it costs nothing.
        /// </summary>
        public Task Prewarm()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.CompletedTask;
#else
            int rows = LastRows;
            return Task.Run(() =>
            {
                _ = Plan;
                if (rows > 0) Setup(rows);
            });
#endif
        }

        /// <summary>The same off the main thread: the bot's test games take a moment on a phone.</summary>
        public Task<FinikMatch3Setup> SetupAsync(int rows)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult(Setup(rows));
#else
            return Task.Run(() => Setup(rows));
#endif
        }
    }

    /// <summary>A board ready to deal: its layout and the moves the player gets on it.</summary>
    public sealed class FinikMatch3Setup
    {
        public readonly FinikMatch3Layout Layout;
        public readonly int Moves;
        /// <summary>Share of the bot's test games that ended inside the moves given (for the tests and the audit).</summary>
        public readonly float BotWinRate;

        public FinikMatch3Setup(FinikMatch3Layout layout, int moves, float botWinRate)
        {
            Layout = layout;
            Moves = moves;
            BotWinRate = botWinRate;
        }
    }

    /// <summary>
    /// Makes the levels of «три в ряд», as many as the player gets through. A level is a pure function
    /// of its number: level 12 is the same board for everyone, every time.
    ///
    /// Difficulty climbs slowly and goes round easy-medium-easy-medium-hard in fives, so a hard level
    /// is always followed by a breather. New things arrive one at a time with a level of their own —
    /// colour goals, a shaped board, crates, ice, tougher crates, thicker ice — and only later mix.
    ///
    /// Every board is played by a simple greedy bot before the child sees it: if the bot cannot clear
    /// it reliably the level is made easier, and the moves are set from how many the bot needed plus a
    /// generous margin, more of it on an easy level than on a hard one.
    /// </summary>
    public static class FinikMatch3Generator
    {
        /// <summary>How many rows a tall screen may add to the level's own.</summary>
        public const int ExtraRows = 4;
        const int BotGames = 12;
        const int BotMoveCap = 90;
        /// <summary>At most this many options are tried per bot move: roughly a casual player, and fast.</summary>
        const int BotOptions = 14;

        static readonly Dictionary<int, FinikMatch3Level> levels = new();
        static readonly Dictionary<long, FinikMatch3Setup> setups = new();

        public static FinikMiniGameLevel Level(int number)
        {
            if (number < 1) return null;
            lock (levels)
            {
                if (!levels.TryGetValue(number, out var level))
                {
                    level = new FinikMatch3Level(number, TierFor(number));
                    levels[number] = level;
                }
                return level;
            }
        }

        /// <summary>1..3. The first three are easy, medium, hard as before; then easy-medium-easy-medium-hard.</summary>
        public static int TierFor(int number)
        {
            if (number <= 3) return Math.Max(1, number);
            return ((number - 4) % 5) switch { 0 => 1, 1 => 2, 2 => 1, 3 => 2, _ => 3 };
        }

        // ------------------------------------------------------------------ plan

        public enum Shape : byte { Rectangle, CornersTop, CornersBottom, Rounded, NotchTop, SideBites, CenterHole, Pillars }
        public enum CratePattern : byte { Bottom, Band, Pillars, Scatter, Block }
        public enum IcePattern : byte { Center, Bottom, Checker, Frame, Corners }

        /// <summary>What a level is, independent of the screen: it decides the goal line the header shows.</summary>
        public sealed class Plan
        {
            public int Number;
            public int Tier;
            public int Seed;
            public int Columns;
            public int BaseRows;
            public int Colors;
            public Shape Shape;
            public int Crates;
            public int StrongCrates;
            public CratePattern CratePattern;
            public int Ice;
            public int ThickIce;
            public IcePattern IcePattern;
            public FinikMatch3Goal[] Goals;
        }

        static int Hash(int a, int b = 0)
        {
            unchecked
            {
                uint x = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ 0x5bd1e995u;
                x ^= x >> 15;
                x *= 0x2C1B3C6Du;
                x ^= x >> 12;
                x *= 0x297A2D39u;
                x ^= x >> 15;
                return (int)(x & 0x7FFFFFFF);
            }
        }

        internal static Plan MakePlan(int number, int tier)
        {
            // A plan the bot cannot get through, or only in a long slog, is scaled down and tried
            // again: a level should take a child a few minutes, not a quarter of an hour.
            float ease = 1f;
            Plan plan = null;
            int longest = tier switch { 1 => 24, 2 => 28, _ => 32 };
            for (int attempt = 0; attempt < 5; attempt++)
            {
                plan = Draft(number, tier, ease);
                var trial = Test(Build(plan, plan.BaseRows), tier, Hash(number, 77 + attempt), 12);
                if (trial.winRate >= 0.9f && trial.moves <= longest) break;
                ease *= 0.8f;
            }
            return plan;
        }

        static Plan Draft(int number, int tier, float ease)
        {
            var rng = new Random(Hash(number, 1));
            float progress = Math.Min(1f, (number - 1) / 40f);
            float scale = (tier switch { 1 => 0.85f, 2 => 1f, _ => 1.2f }) * ease;
            var plan = new Plan
            {
                Number = number,
                Tier = tier,
                Seed = Hash(number, 2),
                Columns = number <= 3 ? 6 : 7,
                BaseRows = number <= 3 ? 6 : 7,
                Colors = number == 1 ? 4 : number <= 16 ? 5 : tier == 3 ? 6 : 5
            };

            int gems = Round((24 + 36 * progress) * scale, 12);
            int perColor = Round((12 + 16 * progress) * scale, 6);
            int crates = Even(Round((6 + 10 * progress) * scale, 4));
            int iceCount = Even(Round((8 + 14 * progress) * scale, 4));

            var kinds = new List<FinikMatch3GoalKind>();
            int colorGoals = 1;
            switch (number)
            {
                case 1: kinds.Add(FinikMatch3GoalKind.Gems); gems = 20; break;
                case 2: kinds.Add(FinikMatch3GoalKind.Color); break;
                case 3: kinds.Add(FinikMatch3GoalKind.Color); colorGoals = 2; plan.Shape = Shape.CornersTop; break;
                case 4: kinds.Add(FinikMatch3GoalKind.Crate); break;
                case 5: kinds.Add(FinikMatch3GoalKind.Crate); kinds.Add(FinikMatch3GoalKind.Color); plan.Shape = Shape.CornersBottom; break;
                case 6: kinds.Add(FinikMatch3GoalKind.Ice); break;
                case 7: kinds.Add(FinikMatch3GoalKind.Ice); kinds.Add(FinikMatch3GoalKind.Gems); plan.Shape = Shape.NotchTop; break;
                default:
                {
                    int count = tier switch { 1 => rng.Next(1, 3), 2 => 2, _ => rng.Next(2, 4) };
                    var pool = new List<(FinikMatch3GoalKind kind, int weight)>
                    {
                        (FinikMatch3GoalKind.Crate, 3), (FinikMatch3GoalKind.Ice, 3), (FinikMatch3GoalKind.Color, 3), (FinikMatch3GoalKind.Gems, 1)
                    };
                    while (kinds.Count < count && pool.Count > 0)
                    {
                        int total = 0;
                        foreach (var entry in pool) total += entry.weight;
                        int roll = rng.Next(total);
                        for (int i = 0; i < pool.Count; i++)
                        {
                            roll -= pool[i].weight;
                            if (roll >= 0) continue;
                            kinds.Add(pool[i].kind);
                            pool.RemoveAt(i);
                            break;
                        }
                    }
                    // Two colours at once only once colours are familiar, and never with three goals already.
                    if (kinds.Contains(FinikMatch3GoalKind.Color) && number >= 10 && kinds.Count < 3 && rng.Next(3) == 0) colorGoals = 2;
                    // Several goals share the moves: each one asks for a bit less.
                    if (kinds.Count > 1)
                    {
                        gems = Round(gems * 0.7f, 10);
                        perColor = Round(perColor * 0.75f, 6);
                        crates = Even(Round(crates * 0.8f, 4));
                        iceCount = Even(Round(iceCount * 0.8f, 4));
                    }
                    var shapes = (Shape[])Enum.GetValues(typeof(Shape));
                    plan.Shape = rng.Next(4) == 0 ? Shape.Rectangle : shapes[1 + rng.Next(shapes.Length - 1)];
                    break;
                }
            }

            var goals = new List<FinikMatch3Goal>();
            if (kinds.Contains(FinikMatch3GoalKind.Crate))
            {
                plan.Crates = crates;
                plan.StrongCrates = number >= 9 ? Even(Round(crates * (0.25f + 0.35f * progress), 0)) : 0;
                var patterns = (CratePattern[])Enum.GetValues(typeof(CratePattern));
                plan.CratePattern = number == 4 ? CratePattern.Bottom : patterns[rng.Next(patterns.Length)];
            }
            if (kinds.Contains(FinikMatch3GoalKind.Ice))
            {
                plan.Ice = iceCount;
                plan.ThickIce = number >= 12 ? Even(Round(iceCount * (0.3f + 0.3f * progress), 0)) : 0;
                var patterns = (IcePattern[])Enum.GetValues(typeof(IcePattern));
                plan.IcePattern = number == 6 ? IcePattern.Center : patterns[rng.Next(patterns.Length)];
            }

            // The placement decides how many crates and ice really fit; the goals count what is there.
            var probe = Build(plan, plan.BaseRows, goals: false);
            plan.Crates = probe.CountCrates();
            plan.Ice = probe.CountIce();

            foreach (var kind in kinds)
            {
                switch (kind)
                {
                    case FinikMatch3GoalKind.Gems:
                        goals.Add(new FinikMatch3Goal(FinikMatch3GoalKind.Gems, gems));
                        break;
                    case FinikMatch3GoalKind.Color:
                        var picked = new List<int>();
                        while (picked.Count < colorGoals)
                        {
                            int c = rng.Next(plan.Colors);
                            if (!picked.Contains(c)) picked.Add(c);
                        }
                        foreach (int c in picked) goals.Add(new FinikMatch3Goal(FinikMatch3GoalKind.Color, perColor, c));
                        break;
                    case FinikMatch3GoalKind.Crate:
                        if (plan.Crates > 0) goals.Add(new FinikMatch3Goal(FinikMatch3GoalKind.Crate, plan.Crates));
                        break;
                    case FinikMatch3GoalKind.Ice:
                        if (plan.Ice > 0) goals.Add(new FinikMatch3Goal(FinikMatch3GoalKind.Ice, plan.Ice));
                        break;
                }
            }
            if (goals.Count == 0) goals.Add(new FinikMatch3Goal(FinikMatch3GoalKind.Gems, gems));
            plan.Goals = goals.ToArray();
            return plan;
        }

        static int Round(float value, int min) => Math.Max(min, (int)Math.Round(value));
        static int Even(int value) => value + (value & 1);

        // ------------------------------------------------------------------ board

        /// <summary>The level's board at <paramref name="rows"/> rows: shape first, then crates, then ice.</summary>
        public static FinikMatch3Layout Build(Plan plan, int rows, bool goals = true)
        {
            int w = plan.Columns, h = Math.Max(plan.BaseRows, rows);
            var rng = new Random(Hash(plan.Seed, h));
            var open = ShapeMask(plan.Shape, w, h, rng);
            var crates = new byte[w * h];
            var ice = new byte[w * h];
            PlaceCrates(plan, w, h, open, crates, rng);
            PlaceIce(plan, w, h, open, crates, ice, rng);
            var layout = new FinikMatch3Layout(w, h, plan.Colors, open, crates, ice, null);
            if (!goals || plan.Goals == null) return layout;
            // The goals count what is really on this board, so they can always be met.
            var fitted = new FinikMatch3Goal[plan.Goals.Length];
            for (int i = 0; i < fitted.Length; i++)
            {
                var goal = plan.Goals[i];
                fitted[i] = goal.Kind switch
                {
                    FinikMatch3GoalKind.Crate => new FinikMatch3Goal(goal.Kind, layout.CountCrates()),
                    FinikMatch3GoalKind.Ice => new FinikMatch3Goal(goal.Kind, layout.CountIce()),
                    _ => goal
                };
            }
            return new FinikMatch3Layout(w, h, plan.Colors, open, crates, ice, fitted);
        }

        static bool[] ShapeMask(Shape shape, int w, int h, Random rng)
        {
            var open = new bool[w * h];
            for (int i = 0; i < open.Length; i++) open[i] = true;
            void Cut(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                open[x + y * w] = false;
                open[w - 1 - x + y * w] = false;
            }
            int mid = (w - 1) / 2;
            switch (shape)
            {
                case Shape.CornersTop:
                    Cut(0, 0); Cut(1, 0); Cut(0, 1);
                    break;
                case Shape.CornersBottom:
                    Cut(0, h - 1); Cut(1, h - 1); Cut(0, h - 2);
                    break;
                case Shape.Rounded:
                    Cut(0, 0); Cut(0, h - 1);
                    if (h >= 8) { Cut(1, 0); Cut(1, h - 1); }
                    break;
                case Shape.NotchTop:
                    // Mirrored, so an even board loses its two middle columns' tops and an odd one its centre.
                    Cut(mid, 0);
                    Cut(mid, 1);
                    break;
                case Shape.SideBites:
                {
                    int y0 = h / 2 - 1;
                    Cut(0, y0); Cut(0, y0 + 1);
                    break;
                }
                case Shape.CenterHole:
                {
                    int y0 = h / 2 - 1;
                    Cut(mid, y0); Cut(mid, y0 + 1);
                    break;
                }
                case Shape.Pillars:
                {
                    int y0 = h / 2 - 1 + rng.Next(2);
                    Cut(1, y0); Cut(1, y0 + 1);
                    break;
                }
            }
            return open;
        }

        static int TopOf(bool[] open, int w, int h, int x)
        {
            for (int y = 0; y < h; y++)
                if (open[x + y * w]) return y;
            return -1;
        }

        /// <summary>Crates go in mirrored pairs, never on a column's top cell, and never fill a row: a gap always feeds below.</summary>
        static void PlaceCrates(Plan plan, int w, int h, bool[] open, byte[] crates, Random rng)
        {
            if (plan.Crates <= 0) return;
            var candidates = new List<int>();
            int Cell(int x, int y) => x + y * w;
            int left = w / 2;
            switch (plan.CratePattern)
            {
                case CratePattern.Bottom:
                    for (int y = h - 1; y >= h - 3; y--)
                    for (int x = 0; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    break;
                case CratePattern.Band:
                {
                    int y0 = h / 2 + 1;
                    for (int y = y0; y < h; y++)
                    for (int x = 1; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    break;
                }
                case CratePattern.Pillars:
                    for (int y = h - 1; y >= 2; y--) candidates.Add(Cell(1, y));
                    for (int y = h - 1; y >= 2; y--) candidates.Add(Cell(left - 1 >= 2 ? left - 1 : 0, y));
                    break;
                case CratePattern.Block:
                    for (int y = h - 1; y >= h / 2; y--)
                    for (int x = left - 1; x < left + (w & 1); x++) candidates.Add(Cell(Math.Max(0, x), y));
                    break;
                case CratePattern.Scatter:
                    for (int y = 2; y < h; y++)
                    for (int x = 0; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    Shuffle(candidates, rng);
                    break;
            }
            // Anything left over goes anywhere legal in the lower two thirds.
            var rest = new List<int>();
            for (int y = h / 3; y < h; y++)
            for (int x = 0; x < left + (w & 1); x++) rest.Add(Cell(x, y));
            Shuffle(rest, rng);
            candidates.AddRange(rest);

            var rowCount = new int[h];
            int placed = 0, strongLeft = plan.StrongCrates;
            foreach (int cell in candidates)
            {
                if (placed >= plan.Crates) break;
                int x = cell % w, y = cell / w, mx = w - 1 - x;
                int a = Cell(x, y), b = Cell(mx, y);
                if (!open[a] || !open[b] || crates[a] > 0) continue;
                if (y == TopOf(open, w, h, x) || y == TopOf(open, w, h, mx)) continue;
                int adds = a == b ? 1 : 2;
                if (rowCount[y] + adds > w - 2) continue;
                if (placed + adds > plan.Crates && placed > 0) continue;
                byte layers = (byte)(strongLeft > 0 ? 2 : 1);
                crates[a] = crates[b] = layers;
                if (layers == 2) strongLeft -= adds;
                rowCount[y] += adds;
                placed += adds;
            }
        }

        static void PlaceIce(Plan plan, int w, int h, bool[] open, byte[] crates, byte[] ice, Random rng)
        {
            if (plan.Ice <= 0) return;
            var candidates = new List<int>();
            int left = w / 2;
            int Cell(int x, int y) => x + y * w;
            float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
            switch (plan.IcePattern)
            {
                case IcePattern.Center:
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    candidates.Sort((p, q) => Distance(p).CompareTo(Distance(q)));
                    break;
                case IcePattern.Bottom:
                    for (int y = h - 1; y >= 0; y--)
                    for (int x = 0; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    break;
                case IcePattern.Checker:
                    for (int y = 1; y < h; y++)
                    for (int x = 0; x < left + (w & 1); x++)
                        if ((x + y) % 2 == 0) candidates.Add(Cell(x, y));
                    break;
                case IcePattern.Frame:
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < left + (w & 1); x++)
                        if (x == 0 || y == 0 || y == h - 1) candidates.Add(Cell(x, y));
                    break;
                case IcePattern.Corners:
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < left + (w & 1); x++) candidates.Add(Cell(x, y));
                    candidates.Sort((p, q) => Distance(q).CompareTo(Distance(p)));
                    break;
            }
            var rest = new List<int>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < left + (w & 1); x++) rest.Add(Cell(x, y));
            Shuffle(rest, rng);
            candidates.AddRange(rest);

            float Distance(int cell) => Math.Abs(cell % w - cx) + Math.Abs(cell / w - cy);

            int placed = 0, thickLeft = plan.ThickIce;
            foreach (int cell in candidates)
            {
                if (placed >= plan.Ice) break;
                int x = cell % w, y = cell / w;
                int a = Cell(x, y), b = Cell(w - 1 - x, y);
                if (!open[a] || !open[b] || crates[a] > 0 || crates[b] > 0 || ice[a] > 0) continue;
                int adds = a == b ? 1 : 2;
                if (placed + adds > plan.Ice && placed > 0) continue;
                byte layers = (byte)(thickLeft > 0 ? 2 : 1);
                ice[a] = ice[b] = layers;
                if (layers == 2) thickLeft -= adds;
                placed += adds;
            }
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ------------------------------------------------------------------ moves

        internal static FinikMatch3Setup Setup(FinikMatch3Level level, int rows)
        {
            var plan = level.Plan;
            rows = Math.Max(plan.BaseRows, Math.Min(rows, plan.BaseRows + ExtraRows));
            long key = ((long)level.Number << 8) | (uint)rows;
            lock (setups)
                if (setups.TryGetValue(key, out var cached)) return cached;

            var layout = Build(plan, rows);
            var trial = Test(layout, plan.Tier, Hash(plan.Seed, rows), level.Number <= FinikMiniGameCatalog.LevelCount ? 14 : 12);
            var setup = new FinikMatch3Setup(layout, trial.moves, trial.winRate);
            lock (setups) setups[key] = setup;
            return setup;
        }

        /// <summary>
        /// Plays the board <see cref="BotGames"/> times and sets the moves from how many the bot needed:
        /// a high percentile of its games, times a margin that is widest on an easy level.
        /// </summary>
        static (int moves, float winRate) Test(FinikMatch3Layout layout, int tier, int seed, int minMoves)
        {
            var used = new List<int>();
            int wins = 0;
            for (int game = 0; game < BotGames; game++)
            {
                var model = new FinikMatch3Model(layout, Hash(seed, game));
                model.Deal();
                int moves = PlayBot(model, BotMoveCap, new Random(Hash(seed, game + 1000)));
                if (moves <= BotMoveCap) wins++;
                used.Add(Math.Min(moves, BotMoveCap));
            }
            used.Sort();
            float percentile = tier switch { 1 => 0.9f, 2 => 0.8f, _ => 0.7f };
            float margin = tier switch { 1 => 1.35f, 2 => 1.2f, _ => 1.1f };
            int pick = used[Math.Min(used.Count - 1, (int)Math.Round(percentile * (used.Count - 1)))];
            int result = (int)Math.Ceiling(pick * margin) + 2;
            return (Math.Max(minMoves, Math.Min(45, result)), wins / (float)BotGames);
        }

        /// <summary>A greedy player: tries a handful of moves on a copy and takes the one that helps the goals most.</summary>
        public static int PlayBot(FinikMatch3Model model, int cap, Random rng)
        {
            for (int move = 1; move <= cap; move++)
            {
                var options = model.Options();
                if (options.Count == 0) return cap + 1;
                // Powers are always in the running; the swaps are sampled, as a person scanning the board would.
                var tried = new List<FinikMatch3Option>();
                var swaps = new List<FinikMatch3Option>();
                foreach (var option in options)
                    if (option.IsTap || model.PieceAt(option.A).Special != FinikMatch3Special.None ||
                        model.PieceAt(option.B).Special != FinikMatch3Special.None)
                        tried.Add(option);
                    else swaps.Add(option);
                Shuffle(swaps, rng);
                for (int i = 0; i < swaps.Count && tried.Count < BotOptions; i++) tried.Add(swaps[i]);

                var best = tried[0];
                float bestScore = float.MinValue;
                foreach (var option in tried)
                {
                    var copy = model.Clone(rng.Next());
                    var turn = copy.Play(option);
                    if (turn == null) continue;
                    float score = Score(model, copy, turn) + (float)rng.NextDouble() * 0.01f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = option;
                    }
                }
                model.Play(best);
                if (model.Won) return move;
            }
            return cap + 1;
        }

        static float Score(FinikMatch3Model before, FinikMatch3Model after, FinikMatch3Turn turn)
        {
            if (after.Won) return 10000f;
            float score = 0f;
            for (int i = 0; i < before.Goals.Count; i++)
            {
                int gained = Math.Min(before.Remaining(i), after.Collected(i) - before.Collected(i));
                float weight = before.Goals[i].Kind switch
                {
                    FinikMatch3GoalKind.Crate => 3f,
                    FinikMatch3GoalKind.Ice => 3f,
                    FinikMatch3GoalKind.Color => 2f,
                    _ => 1f
                };
                score += gained * weight;
            }
            return score + turn.SpecialsMade * 1.5f + turn.GemsCleared * 0.1f;
        }

        // ------------------------------------------------------------------ words

        /// <summary>The goal line over the board: one goal as a sentence, several as a short summary.</summary>
        public static string Describe(IReadOnlyList<FinikMatch3Goal> goals)
        {
            if (goals == null || goals.Count == 0) return string.Empty;
            if (goals.Count > 1) return "Выполни все цели";
            var goal = goals[0];
            return goal.Kind switch
            {
                FinikMatch3GoalKind.Gems => $"Собери {goal.Count} {Plural(goal.Count, "кубик", "кубика", "кубиков")}",
                FinikMatch3GoalKind.Color => $"Собери {goal.Count} {ColorWord(goal.Color, goal.Count)} {Plural(goal.Count, "кубик", "кубика", "кубиков")}",
                FinikMatch3GoalKind.Crate => $"Разбей {goal.Count} {Plural(goal.Count, "ящик", "ящика", "ящиков")}",
                FinikMatch3GoalKind.Ice => $"Растопи {goal.Count} {Plural(goal.Count, "льдинку", "льдинки", "льдинок")}",
                _ => string.Empty
            };
        }

        static readonly string[] ColorOne = { "красный", "синий", "жёлтый", "зелёный", "фиолетовый", "оранжевый" };
        static readonly string[] ColorFew = { "красных", "синих", "жёлтых", "зелёных", "фиолетовых", "оранжевых" };

        static string ColorWord(int color, int count)
        {
            if (color < 0 || color >= ColorFew.Length) return string.Empty;
            int last = count % 10, lastTwo = count % 100;
            return last == 1 && lastTwo != 11 ? ColorOne[color] : ColorFew[color];
        }

        static string Plural(int count, string one, string few, string many)
        {
            int last = count % 10, lastTwo = count % 100;
            if (lastTwo >= 11 && lastTwo <= 14) return many;
            if (last == 1) return one;
            return last >= 2 && last <= 4 ? few : many;
        }
    }
}
