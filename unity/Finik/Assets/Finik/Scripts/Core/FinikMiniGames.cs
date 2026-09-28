using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>Catalog keys of the three mini-games; also what the save file stores.</summary>
    public static class FinikMiniGameId
    {
        public const string Memory = "memory";
        public const string Match3 = "match3";
        public const string Catch = "catch";
    }

    /// <summary>How far the player got in one mini-game.</summary>
    [Serializable]
    public sealed class FinikMiniGameProgress
    {
        public string gameId;
        /// <summary>Highest level cleared, 0 for none: level <c>bestLevel + 1</c> is the one that opens next.</summary>
        public int bestLevel;
        public int plays;
        public long lastPlayedAtMs;
    }

    [Serializable]
    public sealed class FinikMiniGameState
    {
        public List<FinikMiniGameProgress> games = new();

        public FinikMiniGameProgress Find(string gameId)
        {
            if (string.IsNullOrEmpty(gameId) || games == null) return null;
            foreach (var entry in games)
                if (entry != null && string.Equals(entry.gameId, gameId, StringComparison.Ordinal)) return entry;
            return null;
        }

        public FinikMiniGameProgress Ensure(string gameId)
        {
            games ??= new List<FinikMiniGameProgress>();
            var found = Find(gameId);
            if (found != null) return found;
            found = new FinikMiniGameProgress { gameId = gameId };
            games.Add(found);
            return found;
        }

        public int BestLevel(string gameId) => Find(gameId)?.bestLevel ?? 0;
    }

    /// <summary>
    /// One difficulty step of a mini-game. What it is worth never varies by game — the step number
    /// alone decides the mood, the XP and the name — so only the board settings live in the subclasses.
    /// </summary>
    public abstract class FinikMiniGameLevel
    {
        /// <summary>1..3, in the order the hub shows them.</summary>
        public int Number { get; }
        /// <summary>One line over the board while it is played ("Найди 4 пары").</summary>
        public virtual string Goal { get; }

        protected FinikMiniGameLevel(int number, string goal)
        {
            Number = number;
            Goal = goal;
        }

        /// <summary>
        /// How hard the step is, 1..3: easy, medium, hard. For the three fixed steps of a game it is the
        /// step itself; an endless game's generated levels go round easy-medium-hard, so the number
        /// keeps growing while the reward and the colour of the header still say how hard this one is.
        /// </summary>
        public virtual int Tier => Number;

        /// <summary>The level's name on the hub chip: "Лёгкий", or "Уровень 12" for a generated one.</summary>
        public virtual string Title => FinikMiniGameCatalog.TitleFor(Number);
        /// <summary>The difficulty line over the board: "Лёгкий уровень", "Уровень 12 · сложный".</summary>
        public virtual string Heading => $"{Title} уровень";
        /// <summary>Mood the pet gains for clearing it: +3, +6, +9.</summary>
        public int Mood => FinikMiniGameCatalog.MoodFor(Tier);
        /// <summary>XP, granted once ever per game and level.</summary>
        public virtual int Xp => FinikMiniGameCatalog.XpFor(Number);
        /// <summary>True for a level that is a feat in itself: Finik reacts to it with his big joy.</summary>
        public bool IsBig => Tier >= FinikMiniGameCatalog.LevelCount;
    }

    /// <summary>«Найди пару»: an even number of cards on a grid, every picture on exactly two of them.</summary>
    public sealed class FinikMemoryLevel : FinikMiniGameLevel
    {
        public int Cards { get; }
        public int Columns { get; }
        /// <summary>Seconds the whole board is shown face up before it turns over.</summary>
        public float PreviewSeconds { get; }

        public FinikMemoryLevel(int number, int cards, int columns, float previewSeconds, string goal)
            : base(number, goal)
        {
            Cards = cards;
            Columns = columns;
            PreviewSeconds = previewSeconds;
        }

        public int Rows => Cards / Columns;
        public int Pairs => Cards / 2;
    }

    /// <summary>«Лови монетки»: catch the coins in the basket, let the impulse buys fall past it.</summary>
    public sealed class FinikCatchLevel : FinikMiniGameLevel
    {
        /// <summary>Coins that have to be caught.</summary>
        public int Target { get; }
        /// <summary>Misses allowed: a coin that fell through, or a junk buy that landed in the basket.</summary>
        public int Lives { get; }
        /// <summary>Fall speed in board heights per second, at the start of the level and at its end.</summary>
        public float StartSpeed { get; }
        public float EndSpeed { get; }
        /// <summary>Seconds between two drops, at the start of the level and at its end.</summary>
        public float StartInterval { get; }
        public float EndInterval { get; }
        /// <summary>Share of the drops that are junk buys (0..1).</summary>
        public float JunkShare { get; }

        public FinikCatchLevel(int number, int target, int lives, float startSpeed, float endSpeed,
            float startInterval, float endInterval, float junkShare, string goal)
            : base(number, goal)
        {
            Target = target;
            Lives = lives;
            StartSpeed = startSpeed;
            EndSpeed = endSpeed;
            StartInterval = startInterval;
            EndInterval = endInterval;
            JunkShare = junkShare;
        }
    }

    public sealed class FinikMiniGame
    {
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        /// <summary>
        /// How to play, in one short line: shown over the board while a level opens, so a child who
        /// skipped the hub card still knows what to do.
        /// </summary>
        public string Rule { get; }
        /// <summary>Sprite name of the hub card's picture.</summary>
        public string Icon { get; }
        /// <summary>The fixed steps; for an endless game, the first ones its generator makes.</summary>
        public IReadOnlyList<FinikMiniGameLevel> Levels { get; }
        /// <summary>Makes level N of an endless game; null for a game with only its fixed steps.</summary>
        readonly Func<int, FinikMiniGameLevel> generator;

        public FinikMiniGame(string id, string title, string subtitle, string rule, string icon, params FinikMiniGameLevel[] levels)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Rule = rule;
            Icon = icon;
            Levels = levels;
        }

        /// <summary>An endless game: level N comes from <paramref name="makeLevel"/>, as far as the player gets.</summary>
        public FinikMiniGame(string id, string title, string subtitle, string rule, string icon, Func<int, FinikMiniGameLevel> makeLevel)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Rule = rule;
            Icon = icon;
            generator = makeLevel ?? throw new ArgumentNullException(nameof(makeLevel));
            var first = new FinikMiniGameLevel[FinikMiniGameCatalog.LevelCount];
            for (int i = 0; i < first.Length; i++) first[i] = makeLevel(i + 1);
            Levels = first;
        }

        public bool Endless => generator != null;

        public bool HasLevel(int number) => number >= 1 && (Endless || number <= Levels.Count);

        /// <summary>Level <paramref name="number"/> (1-based), or null past the last fixed step.</summary>
        public FinikMiniGameLevel Level(int number)
        {
            if (number < 1) return null;
            if (number <= Levels.Count) return Levels[number - 1];
            return generator?.Invoke(number);
        }

        /// <summary>
        /// The steps the hub card offers. A fixed game shows all of its steps on the card's chips; an
        /// endless one offers just the level the child is on — the first one not cleared yet.
        /// </summary>
        public int[] HubLevels(int bestLevel, int count)
        {
            if (Endless) return new[] { Math.Max(1, bestLevel + 1) };
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = i + 1;
            return result;
        }
    }

    /// <summary>
    /// The three games that lift the pet's mood for free: find the pairs, three in a row and catch
    /// the coins. Every game has the same three steps, worth +3, +6 and +9 mood, and a step opens
    /// once the one before it is cleared — so the biggest reward is the one that was played for.
    /// </summary>
    public static class FinikMiniGameCatalog
    {
        public const int LevelCount = 3;

        /// <summary>Mood for clearing level 1, 2, 3.</summary>
        public static readonly int[] MoodPerLevel = { 3, 6, 9 };
        static readonly int[] XpPerLevel = { 5, 8, 12 };
        static readonly string[] TitlePerLevel = { "Лёгкий", "Средний", "Сложный" };

        /// <summary>The six gem sprites of «три в ряд»; a level takes the first <c>Colors</c> of them.</summary>
        public static readonly string[] GemSprites =
        {
            "game_gem_red", "game_gem_blue", "game_gem_yellow", "game_gem_green", "game_gem_purple", "game_gem_orange"
        };

        /// <summary>Pictures the memory game deals its pairs from; a level takes as many as it needs.</summary>
        public static readonly string[] CardSprites =
        {
            "game_card_ball", "game_card_balloon", "game_card_cupcake", "game_card_rocket",
            "game_card_butterfly", "game_card_guitar", "game_card_boat", "game_card_umbrella",
            "game_card_key", "game_card_flower", "game_card_apple", "game_card_robot"
        };

        /// <summary>What falls past the basket: an impulse buy is a miss, not a catch. Reuses shop art.</summary>
        public static readonly string[] JunkSprites = { "food_fast_food", "shop_ice_cream", "shop_ball" };

        public static readonly IReadOnlyList<FinikMiniGame> Games = new[]
        {
            new FinikMiniGame(FinikMiniGameId.Memory, "Найди пару",
                "Запомни, где какая картинка, и открывай их парами",
                "Запомни картинки, потом открывай по две одинаковые",
                "game_tile_memory",
                new FinikMemoryLevel(1, 8, 4, 2.5f, "Найди 4 пары"),
                new FinikMemoryLevel(2, 12, 4, 3f, "Найди 6 пар"),
                // Twenty cards, not sixteen: a 4x5 grid fills a phone's board, where 4x4 left a band of
                // empty screen above and below it — and ten pairs is a real step up from six.
                new FinikMemoryLevel(3, 20, 4, 4f, "Найди 10 пар")),

            // Endless: every level after the first few comes from the generator, harder bit by bit.
            new FinikMiniGame(FinikMiniGameId.Match3, "Три в ряд",
                "Двигай кубики пальцем, собирай ряды и делай ракеты с бомбами",
                "Проведи кубик к соседнему — собирай по три в ряд",
                "game_tile_match3",
                FinikMatch3Generator.Level),

            new FinikMiniGame(FinikMiniGameId.Catch, "Лови монетки",
                "Веди корзинку пальцем: лови монетки, пропускай лишние покупки",
                "Монетки лови, а покупки пропускай!",
                "game_tile_catch",
                new FinikCatchLevel(1, 8, 3, 0.30f, 0.42f, 1.15f, 0.85f, 0.20f, "Поймай 8 монеток"),
                new FinikCatchLevel(2, 14, 3, 0.38f, 0.58f, 1.00f, 0.70f, 0.28f, "Поймай 14 монеток"),
                new FinikCatchLevel(3, 20, 3, 0.46f, 0.74f, 0.85f, 0.55f, 0.35f, "Поймай 20 монеток"))
        };

        public static bool TryGet(string gameId, out FinikMiniGame game)
        {
            foreach (var candidate in Games)
                if (string.Equals(candidate.Id, gameId, StringComparison.Ordinal))
                {
                    game = candidate;
                    return true;
                }
            game = null;
            return false;
        }

        /// <summary>The level numbered <paramref name="number"/> (1-based), or null.</summary>
        public static FinikMiniGameLevel Level(string gameId, int number) =>
            TryGet(gameId, out var game) ? Level(game, number) : null;

        public static FinikMiniGameLevel Level(FinikMiniGame game, int number) => game?.Level(number);

        public static int MoodFor(int number) =>
            number >= 1 && number <= MoodPerLevel.Length ? MoodPerLevel[number - 1] : 0;

        public static int XpFor(int number) =>
            number >= 1 && number <= XpPerLevel.Length ? XpPerLevel[number - 1] : 0;

        public static string TitleFor(int number) =>
            number >= 1 && number <= TitlePerLevel.Length ? TitlePerLevel[number - 1] : $"Уровень {number}";

        /// <summary>A step opens once the one before it is cleared; the first one is always open.</summary>
        public static bool Unlocked(int number, int bestLevel) => number <= Math.Max(1, bestLevel + 1);
    }
}
