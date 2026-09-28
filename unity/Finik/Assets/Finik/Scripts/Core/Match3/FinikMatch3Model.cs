using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>
    /// A power piece of «три в ряд», made by a bigger match, as in Homescapes:
    /// four in a line give a rocket, a 2x2 square a paper plane, an L or a T a bomb, five in a line
    /// a disco ball. Powers have no colour: a swap with anything, or a tap, sets one off.
    /// </summary>
    public enum FinikMatch3Special : byte
    {
        None,
        /// <summary>Clears its row.</summary>
        RocketH,
        /// <summary>Clears its column.</summary>
        RocketV,
        /// <summary>Knocks out its four neighbours and flies off to one more piece that matters.</summary>
        Plane,
        /// <summary>Clears everything around it, two cells out.</summary>
        Bomb,
        /// <summary>Clears every gem of one colour.</summary>
        Rainbow
    }

    public enum FinikMatch3GoalKind : byte
    {
        /// <summary>Gems of any colour.</summary>
        Gems,
        /// <summary>Gems of one colour.</summary>
        Color,
        /// <summary>Crates broken all the way.</summary>
        Crate,
        /// <summary>Ice melted all the way.</summary>
        Ice
    }

    [Serializable]
    public readonly struct FinikMatch3Goal
    {
        public readonly FinikMatch3GoalKind Kind;
        /// <summary>The gem colour of a <see cref="FinikMatch3GoalKind.Color"/> goal, -1 otherwise.</summary>
        public readonly int Color;
        public readonly int Count;

        public FinikMatch3Goal(FinikMatch3GoalKind kind, int count, int color = -1)
        {
            Kind = kind;
            Count = count;
            Color = color;
        }
    }

    /// <summary>
    /// Everything that is fixed about a board before a gem is dealt: its shape, the crates and the
    /// ice on it, how many colours are in play and what has to be collected. Cells are numbered
    /// <c>x + y * Columns</c>, row 0 at the top.
    /// </summary>
    public sealed class FinikMatch3Layout
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly int Colors;
        /// <summary>False for a hole: nothing is ever there, and pieces fall past it.</summary>
        public readonly bool[] Open;
        /// <summary>Layers of crate on a cell, 0..2. A crate holds no gem and does not fall.</summary>
        public readonly byte[] Crates;
        /// <summary>Layers of ice under the gem of a cell, 0..2.</summary>
        public readonly byte[] Ice;
        public readonly FinikMatch3Goal[] Goals;

        public FinikMatch3Layout(int columns, int rows, int colors, bool[] open, byte[] crates, byte[] ice, FinikMatch3Goal[] goals)
        {
            Columns = columns;
            Rows = rows;
            Colors = colors;
            int n = columns * rows;
            Open = open ?? Fill(n, true);
            Crates = crates ?? new byte[n];
            Ice = ice ?? new byte[n];
            Goals = goals ?? Array.Empty<FinikMatch3Goal>();
            if (Open.Length != n || Crates.Length != n || Ice.Length != n)
                throw new ArgumentException("Layout arrays must have Columns * Rows cells.");
        }

        /// <summary>A plain rectangle with nothing on it.</summary>
        public static FinikMatch3Layout Rectangle(int columns, int rows, int colors, params FinikMatch3Goal[] goals) =>
            new(columns, rows, colors, null, null, null, goals);

        public int Index(int x, int y) => x + y * Columns;

        public int CountCrates()
        {
            int count = 0;
            foreach (byte layers in Crates) if (layers > 0) count++;
            return count;
        }

        public int CountIce()
        {
            int count = 0;
            for (int i = 0; i < Ice.Length; i++) if (Ice[i] > 0 && Open[i]) count++;
            return count;
        }

        static bool[] Fill(int n, bool value)
        {
            var result = new bool[n];
            for (int i = 0; i < n; i++) result[i] = value;
            return result;
        }
    }

    public struct FinikMatch3Piece
    {
        /// <summary>Stable across swaps and falls, so a view can keep one object per piece. 0 = none.</summary>
        public int Id;
        /// <summary>Gem colour, -1 for a power.</summary>
        public int Color;
        public FinikMatch3Special Special;

        public bool Exists => Id != 0;
        public bool IsGem => Id != 0 && Special == FinikMatch3Special.None;
    }

    // ------------------------------------------------------------------ what a turn did, for the view

    /// <summary>One thing the board did during a turn, in the order it has to be shown.</summary>
    public abstract class FinikMatch3Step { }

    public struct FinikMatch3Removal
    {
        public int Id;
        public int Cell;
        public int Color;
        public FinikMatch3Special Special;
        /// <summary>Seconds after the start of the blast.</summary>
        public float Delay;
        /// <summary>The cell a power is being made on, when this gem was part of the match that made it; -1 otherwise.</summary>
        public int MergeInto;
    }

    public struct FinikMatch3Created
    {
        public int Id;
        public int Cell;
        public FinikMatch3Special Special;
        public float Delay;
    }

    /// <summary>A gem turned into a power where it stands (a disco ball swapped with a power does that).</summary>
    public struct FinikMatch3Transform
    {
        public int Id;
        public int Cell;
        public FinikMatch3Special Special;
        public float Delay;
    }

    public struct FinikMatch3ObstacleHit
    {
        public int Cell;
        /// <summary>True for a crate, false for ice.</summary>
        public bool Crate;
        public int LayersLeft;
        public float Delay;
    }

    public enum FinikMatch3EffectKind : byte
    {
        /// <summary>A rocket flying from <c>From</c> to <c>To</c>.</summary>
        Rocket,
        /// <summary>A paper plane flying from <c>From</c> to <c>To</c>.</summary>
        Plane,
        /// <summary>A beam of the disco ball from <c>From</c> to a gem at <c>To</c>.</summary>
        Beam,
        /// <summary>A blast wave round <c>From</c>, <c>Radius</c> cells out.</summary>
        Bomb
    }

    public struct FinikMatch3Effect
    {
        public FinikMatch3EffectKind Kind;
        public int From;
        public int To;
        public float Delay;
        public float Duration;
        public float Radius;
        /// <summary>The colour a disco ball is clearing, -1 when it does not matter.</summary>
        public int Color;
    }

    /// <summary>Everything that was cleared at once, with each piece's own delay inside the blast.</summary>
    public sealed class FinikMatch3Blast : FinikMatch3Step
    {
        /// <summary>1 for what the move itself did, 2+ for chain reactions after the board fell.</summary>
        public int Cascade;
        public readonly List<FinikMatch3Removal> Removed = new();
        public readonly List<FinikMatch3Created> Created = new();
        public readonly List<FinikMatch3Transform> Transformed = new();
        public readonly List<FinikMatch3ObstacleHit> Obstacles = new();
        public readonly List<FinikMatch3Effect> Effects = new();

        /// <summary>When the last thing in the blast is over.</summary>
        public float Duration
        {
            get
            {
                float end = 0f;
                foreach (var r in Removed) end = Math.Max(end, r.Delay);
                foreach (var c in Created) end = Math.Max(end, c.Delay);
                foreach (var o in Obstacles) end = Math.Max(end, o.Delay);
                foreach (var t in Transformed) end = Math.Max(end, t.Delay);
                foreach (var e in Effects) end = Math.Max(end, e.Delay + e.Duration);
                return end;
            }
        }
    }

    public struct FinikMatch3Move
    {
        public int Id;
        /// <summary>Cells the piece passes, first to last. A new piece starts at the top cell of its column.</summary>
        public int[] Path;
        /// <summary>For a new piece, how many new pieces of its column came before it (0 = first); -1 for an old one.</summary>
        public int SpawnRank;
        public int Color;
        public FinikMatch3Special Special;
    }

    /// <summary>Gravity: the pieces sliding into the holes and the new ones dropping in.</summary>
    public sealed class FinikMatch3Fall : FinikMatch3Step
    {
        public readonly List<FinikMatch3Move> Moves = new();
    }

    public struct FinikMatch3Relocation
    {
        public int Id;
        public int From;
        public int To;
        /// <summary>The new colour when the shuffle had to repaint the gem, -1 when it kept its own.</summary>
        public int NewColor;
    }

    /// <summary>The pieces trading places, either by the power or because no move was left.</summary>
    public sealed class FinikMatch3Shuffle : FinikMatch3Step
    {
        /// <summary>True when the board did it by itself because no move was left.</summary>
        public bool Automatic;
        public readonly List<FinikMatch3Relocation> Pieces = new();
    }

    /// <summary>What a swap, a tap or a power did, step by step.</summary>
    public sealed class FinikMatch3Turn
    {
        public readonly List<FinikMatch3Step> Steps = new();
        public int GemsCleared;
        public int SpecialsMade;
    }

    /// <summary>A move the player could make: a swap of two neighbours, or a tap on a power (A == B).</summary>
    public readonly struct FinikMatch3Option
    {
        public readonly int A;
        public readonly int B;

        public FinikMatch3Option(int a, int b)
        {
            A = a;
            B = b;
        }

        public bool IsTap => A == B;
    }

    /// <summary>
    /// The rules of «три в ряд» with no Unity in them, so the board, the level generator's test bot
    /// and the tests all play the same game. Every call that changes the board returns what it did
    /// as a <see cref="FinikMatch3Turn"/>; the view animates that and never decides anything itself.
    ///
    /// Runs of three or more and 2x2 squares clear. A bigger match leaves a power behind, powers set
    /// each other off, two powers swapped together make a combo. Crates break from a match next to
    /// them or a power's hit; ice melts when the gem on it clears. Pieces fall past holes, and a cell
    /// under a crate is filled from the side, so a shaped board never locks up. A board with no move
    /// left shuffles itself for free.
    /// </summary>
    public sealed class FinikMatch3Model
    {
        const float RocketStep = 0.045f;
        const float BombStep = 0.06f;
        const float PlaneFlight = 0.5f;
        const float BeamStep = 0.035f;
        const float ChainDelay = 0.05f;

        readonly int w, h, n;
        readonly int colors;
        readonly bool[] open;
        readonly byte[] crate;
        readonly byte[] ice;
        readonly FinikMatch3Piece[] cells;
        readonly FinikMatch3Goal[] goals;
        readonly int[] collected;
        readonly Random rng;
        int nextId = 1;
        /// <summary>Cells a piece arrived at in the last fall: where a chain reaction's power appears.</summary>
        readonly HashSet<int> recentlyMoved = new();
        /// <summary>The bot's clones skip the event log: they only need the outcome.</summary>
        bool record = true;

        public int Columns => w;
        public int Rows => h;
        public int Colors => colors;
        public IReadOnlyList<FinikMatch3Goal> Goals => goals;

        public FinikMatch3Model(FinikMatch3Layout layout, int seed)
        {
            w = layout.Columns;
            h = layout.Rows;
            n = w * h;
            colors = Math.Max(3, Math.Min(layout.Colors, 6));
            open = (bool[])layout.Open.Clone();
            crate = (byte[])layout.Crates.Clone();
            ice = (byte[])layout.Ice.Clone();
            for (int i = 0; i < n; i++)
                if (!open[i])
                {
                    crate[i] = 0;
                    ice[i] = 0;
                }
            cells = new FinikMatch3Piece[n];
            goals = (FinikMatch3Goal[])layout.Goals.Clone();
            collected = new int[goals.Length];
            rng = new Random(seed);
        }

        FinikMatch3Model(FinikMatch3Model source, int seed)
        {
            w = source.w;
            h = source.h;
            n = source.n;
            colors = source.colors;
            open = source.open;
            crate = (byte[])source.crate.Clone();
            ice = (byte[])source.ice.Clone();
            cells = (FinikMatch3Piece[])source.cells.Clone();
            goals = source.goals;
            collected = (int[])source.collected.Clone();
            GemsCleared = source.GemsCleared;
            nextId = source.nextId;
            rng = new Random(seed);
            record = false;
        }

        /// <summary>A copy for trying moves out: same board, its own dice, no event log.</summary>
        public FinikMatch3Model Clone(int seed) => new(this, seed);

        // ------------------------------------------------------------------ reading the board

        public int Index(int x, int y) => x + y * w;
        public int X(int cell) => cell % w;
        public int Y(int cell) => cell / w;
        public bool IsOpen(int cell) => cell >= 0 && cell < n && open[cell];
        public int CrateAt(int cell) => crate[cell];
        public int IceAt(int cell) => ice[cell];
        public FinikMatch3Piece PieceAt(int cell) => cells[cell];

        /// <summary>Holds a piece that may be swapped: an open cell, no crate on it.</summary>
        public bool IsMovable(int cell) => IsOpen(cell) && crate[cell] == 0 && cells[cell].Exists;

        bool IsGem(int cell) => IsOpen(cell) && crate[cell] == 0 && cells[cell].IsGem;

        int GemColor(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return -1;
            int i = x + y * w;
            return IsGem(i) ? cells[i].Color : -1;
        }

        /// <summary>Gems cleared since the deal, all colours together.</summary>
        public int GemsCleared { get; private set; }

        public int Collected(int goal) => goal >= 0 && goal < collected.Length ? collected[goal] : 0;

        public int Remaining(int goal) =>
            goal >= 0 && goal < goals.Length ? Math.Max(0, goals[goal].Count - collected[goal]) : 0;

        public bool Won
        {
            get
            {
                for (int i = 0; i < goals.Length; i++)
                    if (collected[i] < goals[i].Count) return false;
                return goals.Length > 0;
            }
        }

        /// <summary>All goals together, 0..1: what the bar under the goals shows.</summary>
        public float Progress
        {
            get
            {
                int need = 0, have = 0;
                for (int i = 0; i < goals.Length; i++)
                {
                    need += goals[i].Count;
                    have += Math.Min(goals[i].Count, collected[i]);
                }
                return need == 0 ? 0f : have / (float)need;
            }
        }

        // ------------------------------------------------------------------ dealing

        /// <summary>A fresh board: every free cell gets a gem, no run is on it already, and a move exists.</summary>
        public void Deal()
        {
            for (int guard = 0; guard < 60; guard++)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    cells[i] = default;
                    if (!open[i] || crate[i] > 0) continue;
                    cells[i] = NewGem(DrawWithoutMatch(x, y));
                }
                if (HasMove()) return;
            }
        }

        /// <summary>
        /// Puts pieces exactly where given — for the tests and for tools that set a board up by hand.
        /// <paramref name="colors"/>: a gem colour per cell, or -1 for none; <paramref name="specials"/>
        /// (optional) turns a cell into a power. Cells that are holes or crates stay empty.
        /// </summary>
        public void Load(int[] colors, FinikMatch3Special[] specials = null)
        {
            if (colors == null || colors.Length != n) throw new ArgumentException("One colour per cell.");
            for (int i = 0; i < n; i++)
            {
                cells[i] = default;
                if (!open[i] || crate[i] > 0) continue;
                var special = specials != null ? specials[i] : FinikMatch3Special.None;
                if (special != FinikMatch3Special.None) cells[i] = NewSpecial(special);
                else if (colors[i] >= 0) cells[i] = NewGem(Math.Min(colors[i], this.colors - 1));
            }
            recentlyMoved.Clear();
        }

        FinikMatch3Piece NewGem(int color) => new() { Id = nextId++, Color = color, Special = FinikMatch3Special.None };

        FinikMatch3Piece NewSpecial(FinikMatch3Special special) => new() { Id = nextId++, Color = -1, Special = special };

        /// <summary>A colour that does not finish a run or a square with the cells left of and above it.</summary>
        int DrawWithoutMatch(int x, int y)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int c = rng.Next(colors);
                if (!WouldMatchBehind(x, y, c)) return c;
            }
            return rng.Next(colors);
        }

        bool WouldMatchBehind(int x, int y, int c)
        {
            if (GemColor(x - 1, y) == c && GemColor(x - 2, y) == c) return true;
            if (GemColor(x, y - 1) == c && GemColor(x, y - 2) == c) return true;
            return GemColor(x - 1, y) == c && GemColor(x, y - 1) == c && GemColor(x - 1, y - 1) == c;
        }

        // ------------------------------------------------------------------ moves

        public bool Adjacent(int a, int b)
        {
            if (a < 0 || b < 0 || a >= n || b >= n) return false;
            int dx = Math.Abs(X(a) - X(b)), dy = Math.Abs(Y(a) - Y(b));
            return dx + dy == 1;
        }

        /// <summary>Whether swapping the two would do anything: a power is involved, or a match appears.</summary>
        public bool SwapWorks(int a, int b)
        {
            if (!Adjacent(a, b) || !IsMovable(a) || !IsMovable(b)) return false;
            if (cells[a].Special != FinikMatch3Special.None || cells[b].Special != FinikMatch3Special.None) return true;
            (cells[a], cells[b]) = (cells[b], cells[a]);
            bool any = MatchAt(a) || MatchAt(b);
            (cells[a], cells[b]) = (cells[b], cells[a]);
            return any;
        }

        /// <summary>True when the gem on that cell sits in a run of three or in a 2x2 square.</summary>
        bool MatchAt(int cell)
        {
            if (!IsGem(cell)) return false;
            int c = cells[cell].Color, x = X(cell), y = Y(cell);
            if (Run(x, y, 1, 0, c) + Run(x, y, -1, 0, c) >= 2) return true;
            if (Run(x, y, 0, 1, c) + Run(x, y, 0, -1, c) >= 2) return true;
            for (int dx = -1; dx <= 0; dx++)
            for (int dy = -1; dy <= 0; dy++)
            {
                int sx = x + dx, sy = y + dy;
                if (GemColor(sx, sy) == c && GemColor(sx + 1, sy) == c && GemColor(sx, sy + 1) == c && GemColor(sx + 1, sy + 1) == c)
                    return true;
            }
            return false;
        }

        int Run(int x, int y, int dx, int dy, int c)
        {
            int count = 0;
            for (int k = 1; k < Math.Max(w, h); k++)
            {
                if (GemColor(x + dx * k, y + dy * k) != c) break;
                count++;
            }
            return count;
        }

        /// <summary>True while the player has something to do: a power to tap, or a swap that matches.</summary>
        public bool HasMove()
        {
            for (int i = 0; i < n; i++)
                if (IsMovable(i) && cells[i].Special != FinikMatch3Special.None) return true;
            return FindSwap(out _, out _);
        }

        /// <summary>A swap that makes a match, for the idle hint; false when there is none.</summary>
        public bool FindSwap(out int a, out int b)
        {
            for (int i = 0; i < n; i++)
            {
                int x = X(i), y = Y(i);
                if (x + 1 < w && SwapWorks(i, i + 1)) { a = i; b = i + 1; return true; }
                if (y + 1 < h && SwapWorks(i, i + w)) { a = i; b = i + w; return true; }
            }
            a = b = -1;
            return false;
        }

        /// <summary>Every move that would do something, for the test bot.</summary>
        public List<FinikMatch3Option> Options()
        {
            var list = new List<FinikMatch3Option>();
            for (int i = 0; i < n; i++)
            {
                if (!IsMovable(i)) continue;
                if (cells[i].Special != FinikMatch3Special.None) list.Add(new FinikMatch3Option(i, i));
                int x = X(i), y = Y(i);
                if (x + 1 < w && SwapWorks(i, i + 1)) list.Add(new FinikMatch3Option(i, i + 1));
                if (y + 1 < h && SwapWorks(i, i + w)) list.Add(new FinikMatch3Option(i, i + w));
            }
            return list;
        }

        public FinikMatch3Turn Play(FinikMatch3Option option) => option.IsTap ? Tap(option.A) : Swap(option.A, option.B);

        /// <summary>
        /// The player moved the piece on <paramref name="from"/> onto <paramref name="to"/>. Null when
        /// the swap does nothing: the view slides the two back and it costs no move.
        /// </summary>
        public FinikMatch3Turn Swap(int from, int to)
        {
            if (!SwapWorks(from, to)) return null;
            (cells[from], cells[to]) = (cells[to], cells[from]);
            var moved = cells[to];
            var other = cells[from];
            var start = new List<Activation>();

            bool movedSpecial = moved.Special != FinikMatch3Special.None;
            bool otherSpecial = other.Special != FinikMatch3Special.None;
            if (movedSpecial && otherSpecial)
            {
                // Two powers together: one combo, where the dragged one landed.
                start.Add(Combo(moved, other, from, to));
            }
            else if (movedSpecial || otherSpecial)
            {
                var power = movedSpecial ? moved : other;
                var gem = movedSpecial ? other : moved;
                int at = movedSpecial ? to : from;
                // A disco ball takes the colour it was swapped with; any other power just goes off.
                start.Add(new Activation
                {
                    Cell = at,
                    Kind = power.Special,
                    Color = power.Special == FinikMatch3Special.Rainbow ? gem.Color : -1,
                    Consumes = new[] { at }
                });
            }
            return Resolve(start, to, from);
        }

        /// <summary>A tap on a power sets it off where it stands. Null for a tap on anything else.</summary>
        public FinikMatch3Turn Tap(int cell)
        {
            if (!IsMovable(cell) || cells[cell].Special == FinikMatch3Special.None) return null;
            var start = new List<Activation>
            {
                new() { Cell = cell, Kind = cells[cell].Special, Color = -1, Consumes = new[] { cell } }
            };
            return Resolve(start, -1, -1);
        }

        /// <summary>A power from the dock (hammer, bomb) hits these cells: pieces clear, powers go off, crates crack.</summary>
        public FinikMatch3Turn Strike(IEnumerable<int> targets)
        {
            var start = new List<Activation>();
            foreach (int cell in targets)
                if (IsOpen(cell)) start.Add(new Activation { Cell = cell, Kind = FinikMatch3Special.None, Plain = true });
            return start.Count == 0 ? null : Resolve(start, -1, -1);
        }

        /// <summary>The «Перемешать» power: every piece takes a new place, crates stay where they are.</summary>
        public FinikMatch3Turn ShufflePower()
        {
            var turn = new FinikMatch3Turn();
            var step = ShuffleBoard(automatic: false);
            turn.Steps.Add(step);
            // A shuffle never leaves a match standing, but a dead board after a forced repaint is
            // still possible on a tiny shape; the loop below guarantees a move either way.
            if (!HasMove()) turn.Steps.Add(ShuffleBoard(automatic: true));
            return turn;
        }

        // ------------------------------------------------------------------ combos

        enum ComboKind : byte { None, Cross, BigCross, BigBomb, TriplePlane, PlaneCarry, RainbowSpecial, RainbowAll }

        struct Activation
        {
            public int Cell;
            public FinikMatch3Special Kind;
            /// <summary>Colour a disco ball clears; -1 lets it pick the most common one.</summary>
            public int Color;
            public ComboKind Combo;
            /// <summary>The power a plane carries, or the one a disco ball hands out.</summary>
            public FinikMatch3Special Payload;
            /// <summary>Cells whose pieces this activation uses up at its start (the powers themselves).</summary>
            public int[] Consumes;
            /// <summary>A plain hit from the dock, not a power going off.</summary>
            public bool Plain;
            public float Time;
        }

        static bool IsRocket(FinikMatch3Special s) => s == FinikMatch3Special.RocketH || s == FinikMatch3Special.RocketV;

        Activation Combo(FinikMatch3Piece moved, FinikMatch3Piece other, int from, int to)
        {
            var a = moved.Special;
            var b = other.Special;
            var act = new Activation { Cell = to, Color = -1, Consumes = new[] { to, from } };

            if (a == FinikMatch3Special.Rainbow && b == FinikMatch3Special.Rainbow)
            {
                act.Kind = FinikMatch3Special.Rainbow;
                act.Combo = ComboKind.RainbowAll;
            }
            else if (a == FinikMatch3Special.Rainbow || b == FinikMatch3Special.Rainbow)
            {
                act.Kind = FinikMatch3Special.Rainbow;
                act.Combo = ComboKind.RainbowSpecial;
                act.Payload = a == FinikMatch3Special.Rainbow ? b : a;
            }
            else if (a == FinikMatch3Special.Plane && b == FinikMatch3Special.Plane)
            {
                act.Kind = FinikMatch3Special.Plane;
                act.Combo = ComboKind.TriplePlane;
            }
            else if (a == FinikMatch3Special.Plane || b == FinikMatch3Special.Plane)
            {
                act.Kind = FinikMatch3Special.Plane;
                act.Combo = ComboKind.PlaneCarry;
                act.Payload = a == FinikMatch3Special.Plane ? b : a;
            }
            else if (a == FinikMatch3Special.Bomb && b == FinikMatch3Special.Bomb)
            {
                act.Kind = FinikMatch3Special.Bomb;
                act.Combo = ComboKind.BigBomb;
            }
            else if (IsRocket(a) && IsRocket(b))
            {
                act.Kind = FinikMatch3Special.RocketH;
                act.Combo = ComboKind.Cross;
            }
            else
            {
                // A rocket with a bomb: three rows and three columns at once.
                act.Kind = FinikMatch3Special.RocketH;
                act.Combo = ComboKind.BigCross;
            }
            return act;
        }

        // ------------------------------------------------------------------ resolving a turn

        FinikMatch3Turn Resolve(List<Activation> start, int preferA, int preferB)
        {
            var turn = new FinikMatch3Turn();
            int cascade = 0;
            var pending = start;
            while (true)
            {
                var groups = FindGroups();
                if (groups.Count == 0 && pending.Count == 0) break;
                cascade++;
                var blast = new Blaster(this, record ? new FinikMatch3Blast { Cascade = cascade } : null, turn);
                foreach (var group in groups) blast.Match(group, preferA, preferB);
                foreach (var activation in pending) blast.Schedule(activation);
                blast.Run();
                if (record) turn.Steps.Add(blast.Log);
                pending = new List<Activation>();
                preferA = preferB = -1;
                var fall = Gravity();
                if (record && fall.Moves.Count > 0) turn.Steps.Add(fall);
                // A power that ends up in a gap left by the fall never goes off by itself: only a
                // match can start the next round, so the loop stops once the board is quiet.
                if (cascade > 60) break;
            }
            if (!Won)
                for (int guard = 0; guard < 4 && !HasMove(); guard++)
                    turn.Steps.Add(ShuffleBoard(automatic: true));
            return turn;
        }

        // ------------------------------------------------------------------ matches

        sealed class Pattern
        {
            public readonly List<int> Cells = new();
            public bool Horizontal;
            public bool Square;
        }

        sealed class Group
        {
            public readonly List<int> Cells = new();
            public readonly List<Pattern> Patterns = new();
            public int Color;
        }

        List<Group> FindGroups()
        {
            var patterns = new List<Pattern>();
            for (int y = 0; y < h; y++)
            {
                int x = 0;
                while (x < w)
                {
                    int c = GemColor(x, y), end = x + 1;
                    if (c >= 0)
                        while (end < w && GemColor(end, y) == c) end++;
                    if (c >= 0 && end - x >= 3)
                    {
                        var p = new Pattern { Horizontal = true };
                        for (int k = x; k < end; k++) p.Cells.Add(k + y * w);
                        patterns.Add(p);
                    }
                    x = end;
                }
            }
            for (int x = 0; x < w; x++)
            {
                int y = 0;
                while (y < h)
                {
                    int c = GemColor(x, y), end = y + 1;
                    if (c >= 0)
                        while (end < h && GemColor(x, end) == c) end++;
                    if (c >= 0 && end - y >= 3)
                    {
                        var p = new Pattern { Horizontal = false };
                        for (int k = y; k < end; k++) p.Cells.Add(x + k * w);
                        patterns.Add(p);
                    }
                    y = end;
                }
            }
            for (int y = 0; y + 1 < h; y++)
            for (int x = 0; x + 1 < w; x++)
            {
                int c = GemColor(x, y);
                if (c < 0 || GemColor(x + 1, y) != c || GemColor(x, y + 1) != c || GemColor(x + 1, y + 1) != c) continue;
                var p = new Pattern { Square = true };
                p.Cells.Add(x + y * w);
                p.Cells.Add(x + 1 + y * w);
                p.Cells.Add(x + (y + 1) * w);
                p.Cells.Add(x + 1 + (y + 1) * w);
                patterns.Add(p);
            }
            if (patterns.Count == 0) return new List<Group>();

            // Patterns that share a cell are one match: an L is a row and a column meeting.
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = -1;
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            foreach (var p in patterns)
            {
                foreach (int cell in p.Cells)
                    if (parent[cell] < 0) parent[cell] = cell;
                int root = Find(p.Cells[0]);
                for (int k = 1; k < p.Cells.Count; k++)
                {
                    int other = Find(p.Cells[k]);
                    if (other != root) parent[other] = root;
                }
            }

            var byRoot = new Dictionary<int, Group>();
            var result = new List<Group>();
            for (int i = 0; i < n; i++)
            {
                if (parent[i] < 0) continue;
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var group))
                {
                    group = new Group { Color = cells[i].Color };
                    byRoot[root] = group;
                    result.Add(group);
                }
                group.Cells.Add(i);
            }
            foreach (var p in patterns) byRoot[Find(p.Cells[0])].Patterns.Add(p);
            return result;
        }

        /// <summary>Homescapes' order: five in a line beats an L, an L beats four, four beats a square.</summary>
        static FinikMatch3Special Classify(Group group, out Pattern main)
        {
            main = null;
            int longest = 0;
            bool horizontal = false, vertical = false, square = false;
            foreach (var p in group.Patterns)
            {
                if (p.Square)
                {
                    square = true;
                    continue;
                }
                if (p.Horizontal) horizontal = true;
                else vertical = true;
                if (p.Cells.Count > longest)
                {
                    longest = p.Cells.Count;
                    main = p;
                }
            }
            if (longest >= 5) return FinikMatch3Special.Rainbow;
            if (horizontal && vertical) return FinikMatch3Special.Bomb;
            if (longest == 4) return main.Horizontal ? FinikMatch3Special.RocketH : FinikMatch3Special.RocketV;
            if (square) return FinikMatch3Special.Plane;
            return FinikMatch3Special.None;
        }

        /// <summary>Where a match's power appears: where the player's gem landed, else where a falling one did.</summary>
        int SpawnCell(Group group, Pattern main, FinikMatch3Special special, int preferA, int preferB)
        {
            if (preferA >= 0 && group.Cells.Contains(preferA)) return preferA;
            if (preferB >= 0 && group.Cells.Contains(preferB)) return preferB;

            if (special == FinikMatch3Special.Bomb)
            {
                // The corner of the L: the cell a row and a column share.
                foreach (var p in group.Patterns)
                {
                    if (p.Square || !p.Horizontal) continue;
                    foreach (var q in group.Patterns)
                    {
                        if (q.Square || q.Horizontal) continue;
                        foreach (int cell in p.Cells)
                            if (q.Cells.Contains(cell)) return cell;
                    }
                }
            }
            var pool = main != null && special != FinikMatch3Special.Plane ? main.Cells : group.Cells;
            int best = -1;
            foreach (int cell in pool)
                if (recentlyMoved.Contains(cell) && (best < 0 || Y(cell) > Y(best))) best = cell;
            if (best >= 0) return best;
            return pool[pool.Count / 2];
        }

        // ------------------------------------------------------------------ the blast

        /// <summary>
        /// One round of clearing. Hits and power activations are queued by time, so a rocket crossing
        /// a bomb sets it off exactly when it gets there, and the view can play it back at that pace.
        /// </summary>
        sealed class Blaster
        {
            readonly FinikMatch3Model m;
            public readonly FinikMatch3Blast Log;
            readonly FinikMatch3Turn turn;
            readonly bool[] hit;
            readonly bool[] targeted;
            readonly List<(float time, int seq, int cell, bool isHit, Activation act)> queue = new();
            int seq;

            public Blaster(FinikMatch3Model model, FinikMatch3Blast log, FinikMatch3Turn turn)
            {
                m = model;
                Log = log;
                this.turn = turn;
                hit = new bool[m.n];
                targeted = new bool[m.n];
            }

            public void Match(Group group, int preferA, int preferB)
            {
                var special = Classify(group, out var main);
                int spawn = special == FinikMatch3Special.None ? -1 : m.SpawnCell(group, main, special, preferA, preferB);
                var near = new HashSet<int>();
                foreach (int cell in group.Cells)
                {
                    RemovePiece(cell, 0f, spawn);
                    hit[cell] = true;
                    int x = m.X(cell), y = m.Y(cell);
                    if (x > 0) near.Add(cell - 1);
                    if (x < m.w - 1) near.Add(cell + 1);
                    if (y > 0) near.Add(cell - m.w);
                    if (y < m.h - 1) near.Add(cell + m.w);
                }
                // A match cracks the crates it touches, one layer each.
                foreach (int cell in near)
                    if (m.IsOpen(cell) && m.crate[cell] > 0 && !hit[cell])
                    {
                        hit[cell] = true;
                        DamageCrate(cell, 0.05f);
                    }
                if (spawn >= 0)
                {
                    m.cells[spawn] = m.NewSpecial(special);
                    turn.SpecialsMade++;
                    Log?.Created.Add(new FinikMatch3Created { Id = m.cells[spawn].Id, Cell = spawn, Special = special, Delay = 0.12f });
                }
            }

            public void Schedule(Activation act)
            {
                if (act.Plain)
                {
                    Enqueue(act.Time, act.Cell, true, default);
                    return;
                }
                if (act.Consumes != null)
                    foreach (int cell in act.Consumes)
                    {
                        if (!m.cells[cell].Exists) continue;
                        RemovePiece(cell, act.Time, act.Consumes.Length > 1 ? act.Cell : -1);
                        hit[cell] = true;
                    }
                Enqueue(act.Time, act.Cell, false, act);
            }

            void Enqueue(float time, int cell, bool isHit, Activation act) => queue.Add((time, seq++, cell, isHit, act));

            public void Run()
            {
                int guard = 0;
                while (queue.Count > 0 && guard++ < 20000)
                {
                    int best = 0;
                    for (int i = 1; i < queue.Count; i++)
                        if (queue[i].time < queue[best].time || (queue[i].time == queue[best].time && queue[i].seq < queue[best].seq))
                            best = i;
                    var item = queue[best];
                    queue.RemoveAt(best);
                    if (item.isHit) Hit(item.cell, item.time);
                    else Fire(item.act, item.time);
                }
            }

            void Hit(int cell, float time)
            {
                if (!m.IsOpen(cell) || hit[cell]) return;
                hit[cell] = true;
                if (m.crate[cell] > 0)
                {
                    DamageCrate(cell, time);
                    return;
                }
                var piece = m.cells[cell];
                if (!piece.Exists) return;
                RemovePiece(cell, time, -1);
                if (piece.Special != FinikMatch3Special.None)
                    Enqueue(time + ChainDelay, cell, false,
                        new Activation { Cell = cell, Kind = piece.Special, Color = -1, Time = time + ChainDelay });
            }

            void RemovePiece(int cell, float time, int mergeInto)
            {
                var piece = m.cells[cell];
                if (!piece.Exists) return;
                m.cells[cell] = default;
                Log?.Removed.Add(new FinikMatch3Removal
                {
                    Id = piece.Id, Cell = cell, Color = piece.Color, Special = piece.Special, Delay = time, MergeInto = mergeInto
                });
                if (piece.IsGem)
                {
                    turn.GemsCleared++;
                    m.GemsCleared++;
                    m.CollectGem(piece.Color);
                }
                if (m.ice[cell] > 0)
                {
                    m.ice[cell]--;
                    Log?.Obstacles.Add(new FinikMatch3ObstacleHit { Cell = cell, Crate = false, LayersLeft = m.ice[cell], Delay = time });
                    if (m.ice[cell] == 0) m.Collect(FinikMatch3GoalKind.Ice, -1);
                }
            }

            void DamageCrate(int cell, float time)
            {
                m.crate[cell]--;
                Log?.Obstacles.Add(new FinikMatch3ObstacleHit { Cell = cell, Crate = true, LayersLeft = m.crate[cell], Delay = time });
                if (m.crate[cell] == 0) m.Collect(FinikMatch3GoalKind.Crate, -1);
            }

            void Effect(FinikMatch3EffectKind kind, int from, int to, float delay, float duration, float radius = 0f, int color = -1) =>
                Log?.Effects.Add(new FinikMatch3Effect
                {
                    Kind = kind, From = from, To = to, Delay = delay, Duration = duration, Radius = radius, Color = color
                });

            void Fire(Activation act, float t)
            {
                int cx = m.X(act.Cell), cy = m.Y(act.Cell);
                switch (act.Combo)
                {
                    case ComboKind.Cross:
                        Row(cy, cx, t, act.Cell);
                        Column(cx, cy, t, act.Cell);
                        return;
                    case ComboKind.BigCross:
                        for (int d = -1; d <= 1; d++)
                        {
                            if (cy + d >= 0 && cy + d < m.h) Row(cy + d, cx, t, m.Index(cx, cy + d));
                            if (cx + d >= 0 && cx + d < m.w) Column(cx + d, cy, t, m.Index(cx + d, cy));
                        }
                        return;
                    case ComboKind.BigBomb:
                        Bomb(act.Cell, 3, t);
                        return;
                    case ComboKind.TriplePlane:
                        Neighbours(act.Cell, t);
                        for (int k = 0; k < 3; k++) Plane(act.Cell, t, FinikMatch3Special.None);
                        return;
                    case ComboKind.PlaneCarry:
                        Neighbours(act.Cell, t);
                        Plane(act.Cell, t, act.Payload);
                        return;
                    case ComboKind.RainbowAll:
                        for (int i = 0; i < m.n; i++)
                        {
                            if (!m.IsOpen(i)) continue;
                            float d = Math.Abs(m.X(i) - cx) + Math.Abs(m.Y(i) - cy);
                            Enqueue(t + 0.1f + d * 0.04f, i, true, default);
                            if (m.cells[i].Exists && d > 0) Effect(FinikMatch3EffectKind.Beam, act.Cell, i, t, 0.1f + d * 0.04f, 0f, m.cells[i].Color);
                        }
                        return;
                    case ComboKind.RainbowSpecial:
                        Rainbow(act.Cell, m.MostCommonColor(hit), t, act.Payload);
                        return;
                }

                switch (act.Kind)
                {
                    case FinikMatch3Special.RocketH:
                        Row(cy, cx, t, act.Cell);
                        break;
                    case FinikMatch3Special.RocketV:
                        Column(cx, cy, t, act.Cell);
                        break;
                    case FinikMatch3Special.Bomb:
                        Bomb(act.Cell, 2, t);
                        break;
                    case FinikMatch3Special.Plane:
                        Neighbours(act.Cell, t);
                        Plane(act.Cell, t, FinikMatch3Special.None);
                        break;
                    case FinikMatch3Special.Rainbow:
                        Rainbow(act.Cell, act.Color >= 0 ? act.Color : m.MostCommonColor(hit), t, FinikMatch3Special.None);
                        break;
                }
            }

            /// <summary>A rocket along row <paramref name="y"/>, flying both ways from column <paramref name="fromX"/>.</summary>
            void Row(int y, int fromX, float t, int effectFrom)
            {
                for (int x = 0; x < m.w; x++) Enqueue(t + Math.Abs(x - fromX) * RocketStep, x + y * m.w, true, default);
                Effect(FinikMatch3EffectKind.Rocket, effectFrom, y * m.w, t, fromX * RocketStep + 0.05f);
                Effect(FinikMatch3EffectKind.Rocket, effectFrom, m.w - 1 + y * m.w, t, (m.w - 1 - fromX) * RocketStep + 0.05f);
            }

            void Column(int x, int fromY, float t, int effectFrom)
            {
                for (int y = 0; y < m.h; y++) Enqueue(t + Math.Abs(y - fromY) * RocketStep, x + y * m.w, true, default);
                Effect(FinikMatch3EffectKind.Rocket, effectFrom, x, t, fromY * RocketStep + 0.05f);
                Effect(FinikMatch3EffectKind.Rocket, effectFrom, x + (m.h - 1) * m.w, t, (m.h - 1 - fromY) * RocketStep + 0.05f);
            }

            void Bomb(int center, int radius, float t)
            {
                int cx = m.X(center), cy = m.Y(center);
                for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Round, not square: the far corners stay.
                    if (Math.Abs(dx) == radius && Math.Abs(dy) == radius) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= m.w || y >= m.h) continue;
                    Enqueue(t + Math.Max(Math.Abs(dx), Math.Abs(dy)) * BombStep, x + y * m.w, true, default);
                }
                Effect(FinikMatch3EffectKind.Bomb, center, center, t, radius * BombStep + 0.1f, radius);
            }

            void Neighbours(int cell, float t)
            {
                int x = m.X(cell), y = m.Y(cell);
                if (x > 0) Enqueue(t + 0.03f, cell - 1, true, default);
                if (x < m.w - 1) Enqueue(t + 0.03f, cell + 1, true, default);
                if (y > 0) Enqueue(t + 0.03f, cell - m.w, true, default);
                if (y < m.h - 1) Enqueue(t + 0.03f, cell + m.w, true, default);
            }

            void Plane(int from, float t, FinikMatch3Special payload)
            {
                int target = m.PlaneTarget(from, hit, targeted);
                if (target < 0) return;
                targeted[target] = true;
                Effect(FinikMatch3EffectKind.Plane, from, target, t, PlaneFlight);
                float land = t + PlaneFlight;
                if (payload == FinikMatch3Special.None)
                {
                    Enqueue(land, target, true, default);
                    return;
                }
                // The plane drops the power it carried where it lands.
                Enqueue(land, target, true, default);
                Enqueue(land + 0.01f, target, false, new Activation { Cell = target, Kind = payload, Color = -1, Time = land + 0.01f });
            }

            void Rainbow(int from, int color, float t, FinikMatch3Special payload)
            {
                if (color < 0) return;
                var targets = new List<int>();
                for (int i = 0; i < m.n; i++)
                    if (!hit[i] && m.IsGem(i) && m.cells[i].Color == color) targets.Add(i);
                int fx = m.X(from), fy = m.Y(from);
                targets.Sort((a, b) =>
                    (Math.Abs(m.X(a) - fx) + Math.Abs(m.Y(a) - fy)).CompareTo(Math.Abs(m.X(b) - fx) + Math.Abs(m.Y(b) - fy)));

                for (int k = 0; k < targets.Count; k++)
                {
                    int cell = targets[k];
                    float at = t + 0.12f + k * BeamStep;
                    Effect(FinikMatch3EffectKind.Beam, from, cell, t + k * BeamStep, 0.12f, 0f, color);
                    if (payload == FinikMatch3Special.None)
                    {
                        Enqueue(at, cell, true, default);
                        continue;
                    }
                    // Every gem of the colour becomes the other power, then they all go off in turn.
                    var kind = IsRocket(payload) ? (m.rng.Next(2) == 0 ? FinikMatch3Special.RocketH : FinikMatch3Special.RocketV) : payload;
                    var piece = m.cells[cell];
                    piece.Special = kind;
                    piece.Color = -1;
                    m.cells[cell] = piece;
                    Log?.Transformed.Add(new FinikMatch3Transform { Id = piece.Id, Cell = cell, Special = kind, Delay = at });
                }
                if (payload == FinikMatch3Special.None) return;
                float go = t + 0.12f + targets.Count * BeamStep + 0.25f;
                for (int k = 0; k < targets.Count; k++) Enqueue(go + k * 0.09f, targets[k], true, default);
            }
        }

        /// <summary>Crates and ice: every goal of that kind counts it.</summary>
        void Collect(FinikMatch3GoalKind kind, int color)
        {
            for (int i = 0; i < goals.Length; i++)
                if (goals[i].Kind == kind) collected[i]++;
        }

        /// <summary>
        /// A cleared gem counts for exactly one goal: its own colour while that goal still wants gems,
        /// otherwise "any gem". The view sends each gem flying into one goal by the same rule, so the
        /// goal card and the model always agree.
        /// </summary>
        void CollectGem(int color)
        {
            int any = -1;
            for (int i = 0; i < goals.Length; i++)
            {
                if (goals[i].Kind == FinikMatch3GoalKind.Color && goals[i].Color == color && collected[i] < goals[i].Count)
                {
                    collected[i]++;
                    return;
                }
                if (goals[i].Kind == FinikMatch3GoalKind.Gems && any < 0) any = i;
            }
            if (any >= 0) collected[any]++;
        }

        int MostCommonColor(bool[] skip)
        {
            var counts = new int[colors];
            for (int i = 0; i < n; i++)
                if (!skip[i] && IsGem(i)) counts[cells[i].Color]++;
            // Goal colours first: a disco ball should help, not just make noise.
            int best = -1;
            for (int c = 0; c < colors; c++)
            {
                if (counts[c] == 0) continue;
                int score = counts[c] + (NeedsColor(c) ? 100 : 0);
                if (best < 0 || score > counts[best] + (NeedsColor(best) ? 100 : 0)) best = c;
            }
            return best;
        }

        bool NeedsColor(int color)
        {
            for (int i = 0; i < goals.Length; i++)
                if (goals[i].Kind == FinikMatch3GoalKind.Color && goals[i].Color == color && collected[i] < goals[i].Count) return true;
            return false;
        }

        bool Needs(FinikMatch3GoalKind kind)
        {
            for (int i = 0; i < goals.Length; i++)
                if (goals[i].Kind == kind && collected[i] < goals[i].Count) return true;
            return false;
        }

        /// <summary>What a plane goes for: a crate or ice the goals still want, a goal colour, else any piece.</summary>
        int PlaneTarget(int from, bool[] hit, bool[] targeted)
        {
            bool crates = Needs(FinikMatch3GoalKind.Crate), iceWanted = Needs(FinikMatch3GoalKind.Ice);
            var best = new List<int>();
            int bestScore = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (i == from || !open[i] || hit[i] || targeted[i]) continue;
                int score;
                if (crate[i] > 0) score = crates ? 30 : 1;
                else if (!cells[i].Exists) continue;
                else if (ice[i] > 0 && iceWanted) score = 25;
                else if (cells[i].IsGem && NeedsColor(cells[i].Color)) score = 20;
                else score = cells[i].Special != FinikMatch3Special.None ? 2 : 5;
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Clear();
                }
                if (score == bestScore) best.Add(i);
            }
            return best.Count == 0 ? -1 : best[rng.Next(best.Count)];
        }

        // ------------------------------------------------------------------ gravity

        int Below(int cell)
        {
            int x = X(cell);
            for (int y = Y(cell) + 1; y < h; y++)
            {
                int i = x + y * w;
                if (open[i]) return i;
            }
            return -1;
        }

        int Above(int cell)
        {
            int x = X(cell);
            for (int y = Y(cell) - 1; y >= 0; y--)
            {
                int i = x + y * w;
                if (open[i]) return i;
            }
            return -1;
        }

        int TopOf(int x)
        {
            for (int y = 0; y < h; y++)
                if (open[x + y * w]) return x + y * w;
            return -1;
        }

        bool Free(int cell) => cell >= 0 && open[cell] && crate[cell] == 0 && !cells[cell].Exists;

        /// <summary>Will something come down the column into this empty cell — a piece above, or a new one?</summary>
        bool Fed(int cell)
        {
            int up = Above(cell);
            while (up >= 0)
            {
                if (crate[up] > 0) return false;
                if (cells[up].Exists) return true;
                up = Above(up);
            }
            return true;
        }

        /// <summary>
        /// Everything falls as far as it can, the tops of the columns fill with new gems, and a cell
        /// cut off from above by a crate is filled from the upper diagonal. Each piece's whole path is
        /// kept, so the view can slide it along the same route.
        /// </summary>
        FinikMatch3Fall Gravity()
        {
            var fall = new FinikMatch3Fall();
            var paths = new Dictionary<int, List<int>>();
            var spawnRank = new Dictionary<int, int>();
            var spawnedInColumn = new int[w];
            recentlyMoved.Clear();

            void Step(int id, int from, int to)
            {
                if (!paths.TryGetValue(id, out var path))
                {
                    path = new List<int> { from };
                    paths[id] = path;
                }
                path.Add(to);
            }

            for (int guard = 0; guard < n * 8; guard++)
            {
                bool moved = false;
                for (int y = h - 2; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    if (!IsMovable(i)) continue;
                    int at = i;
                    for (int next = Below(at); Free(next); next = Below(at))
                    {
                        Step(cells[i].Id, at, next);
                        at = next;
                    }
                    if (at == i) continue;
                    cells[at] = cells[i];
                    cells[i] = default;
                    moved = true;
                }
                for (int x = 0; x < w; x++)
                {
                    int top = TopOf(x);
                    if (!Free(top)) continue;
                    cells[top] = NewGem(rng.Next(colors));
                    spawnRank[cells[top].Id] = spawnedInColumn[x]++;
                    paths[cells[top].Id] = new List<int> { top };
                    moved = true;
                }
                if (moved) continue;

                // Nothing comes straight down any more: slide one piece in from the side, then let
                // the columns settle again. One at a time keeps the order of the slides natural.
                for (int y = h - 1; y >= 1 && !moved; y--)
                for (int x = 0; x < w && !moved; x++)
                {
                    int i = x + y * w;
                    if (!Free(i) || Fed(i)) continue;
                    int first = (x + y) % 2 == 0 ? -1 : 1;
                    for (int k = 0; k < 2 && !moved; k++)
                    {
                        int sx = x + (k == 0 ? first : -first);
                        if (sx < 0 || sx >= w) continue;
                        int source = sx + (y - 1) * w;
                        if (!IsMovable(source)) continue;
                        Step(cells[source].Id, source, i);
                        cells[i] = cells[source];
                        cells[source] = default;
                        moved = true;
                    }
                }
                if (!moved) break;
            }

            for (int i = 0; i < n; i++)
            {
                var piece = cells[i];
                if (!piece.Exists || !paths.TryGetValue(piece.Id, out var path)) continue;
                recentlyMoved.Add(i);
                if (!record) continue;
                fall.Moves.Add(new FinikMatch3Move
                {
                    Id = piece.Id,
                    Path = path.ToArray(),
                    SpawnRank = spawnRank.TryGetValue(piece.Id, out int rank) ? rank : -1,
                    Color = piece.Color,
                    Special = piece.Special
                });
            }
            return fall;
        }

        // ------------------------------------------------------------------ shuffle

        /// <summary>
        /// Deals the pieces already on the board to new places, crates and holes untouched, so that no
        /// match is standing and a move exists. Only a board that cannot be arranged that way — very few
        /// pieces, or nearly one colour — gets some gems repainted.
        /// </summary>
        FinikMatch3Shuffle ShuffleBoard(bool automatic)
        {
            var step = new FinikMatch3Shuffle { Automatic = automatic };
            var places = new List<int>();
            var pieces = new List<FinikMatch3Piece>();
            var origin = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (!IsMovable(i)) continue;
                places.Add(i);
                pieces.Add(cells[i]);
                origin[cells[i].Id] = i;
            }
            if (places.Count == 0) return step;

            bool placed = false;
            for (int attempt = 0; attempt < 80 && !placed; attempt++)
            {
                var pool = new List<FinikMatch3Piece>(pieces);
                foreach (int cell in places) cells[cell] = default;
                foreach (int cell in places)
                {
                    int x = X(cell), y = Y(cell);
                    int start = rng.Next(pool.Count), pick = -1;
                    for (int k = 0; k < pool.Count; k++)
                    {
                        int j = (start + k) % pool.Count;
                        if (!pool[j].IsGem || !WouldMatchBehind(x, y, pool[j].Color))
                        {
                            pick = j;
                            break;
                        }
                    }
                    if (pick < 0) pick = start;
                    cells[cell] = pool[pick];
                    pool[pick] = pool[pool.Count - 1];
                    pool.RemoveAt(pool.Count - 1);
                }
                placed = FindGroups().Count == 0 && HasMove();
            }

            var repainted = new HashSet<int>();
            if (!placed)
            {
                for (int guard = 0; guard < 40; guard++)
                {
                    foreach (int cell in places)
                    {
                        if (!cells[cell].IsGem) continue;
                        var piece = cells[cell];
                        piece.Color = DrawWithoutMatch(X(cell), Y(cell));
                        cells[cell] = piece;
                        repainted.Add(piece.Id);
                    }
                    if (FindGroups().Count == 0 && HasMove()) break;
                }
            }

            foreach (int cell in places)
            {
                var piece = cells[cell];
                step.Pieces.Add(new FinikMatch3Relocation
                {
                    Id = piece.Id,
                    From = origin[piece.Id],
                    To = cell,
                    NewColor = repainted.Contains(piece.Id) ? piece.Color : -1
                });
            }
            recentlyMoved.Clear();
            return step;
        }
    }
}
