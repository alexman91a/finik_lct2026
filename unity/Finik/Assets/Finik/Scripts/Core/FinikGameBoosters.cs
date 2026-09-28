using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>
    /// One super power of a mood game. Powers are free and handed out fresh with every level — three
    /// charges of each — so they are a way out of a stuck board, never something to save up or buy.
    /// </summary>
    public sealed class FinikBooster
    {
        public string Id { get; }
        public string Title { get; }
        /// <summary>Sprite name of the icon inside its slot.</summary>
        public string Icon { get; }
        /// <summary>One line the game shows while the power is armed or right after it fires.</summary>
        public string Hint { get; }
        /// <summary>True when the power needs the player to pick a piece first (hammer, bomb).</summary>
        public bool NeedsTarget { get; }

        public FinikBooster(string id, string title, string icon, string hint, bool needsTarget = false)
        {
            Id = id;
            Title = title;
            Icon = icon;
            Hint = hint;
            NeedsTarget = needsTarget;
        }
    }

    public static class FinikBoosterId
    {
        // Найди пару
        public const string Peek = "peek";
        public const string Pair = "pair";
        public const string Reshuffle = "reshuffle";
        // Три в ряд
        public const string Hammer = "hammer";
        public const string Bomb = "bomb";
        public const string Mix = "mix";
        // Лови монетки
        public const string Magnet = "magnet";
        public const string Shield = "shield";
        public const string Slow = "slow";
    }

    /// <summary>The three powers each game hands out, and how many charges of each a level starts with.</summary>
    public static class FinikBoosterCatalog
    {
        public const int ChargesPerLevel = 3;

        static readonly FinikBooster[] Memory =
        {
            new(FinikBoosterId.Peek, "Подсказка", "booster_hint", "Все карточки открыты на пару секунд — запоминай!"),
            new(FinikBoosterId.Pair, "Открыть пару", "booster_reveal", "Финик сам нашёл одну пару."),
            new(FinikBoosterId.Reshuffle, "Перемешать", "booster_shuffle", "Закрытые карточки поменялись местами.")
        };

        static readonly FinikBooster[] Match3 =
        {
            new(FinikBoosterId.Hammer, "Молоток", "booster_hammer", "Выбери кубик — он разлетится.", needsTarget: true),
            new(FinikBoosterId.Bomb, "Бомба", "booster_bomb", "Выбери кубик — взорвутся все вокруг него.", needsTarget: true),
            new(FinikBoosterId.Mix, "Перемешать", "booster_rainbow", "Поле перемешалось.")
        };

        static readonly FinikBooster[] Catch =
        {
            new(FinikBoosterId.Magnet, "Магнит", "booster_magnet", "Монетки сами летят в корзинку!"),
            new(FinikBoosterId.Shield, "Щит", "booster_shield", "Следующий промах не считается."),
            new(FinikBoosterId.Slow, "Замедление", "booster_clock", "Всё падает медленнее.")
        };

        static readonly FinikBooster[] None = Array.Empty<FinikBooster>();

        public static IReadOnlyList<FinikBooster> For(string gameId) => gameId switch
        {
            FinikMiniGameId.Memory => Memory,
            FinikMiniGameId.Match3 => Match3,
            FinikMiniGameId.Catch => Catch,
            _ => None
        };

        /// <summary>Every icon the catalog names, for the art table.</summary>
        public static IEnumerable<string> Icons()
        {
            foreach (var set in new[] { Memory, Match3, Catch })
            foreach (var booster in set)
                yield return booster.Icon;
        }
    }
}
